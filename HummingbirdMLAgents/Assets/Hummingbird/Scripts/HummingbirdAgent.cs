using System;
using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// A hummingbird Machine Learning Agent (a special class that makes decisions using NNs)
/// </summary>
public class HummingbirdAgent : Agent
{
    [Tooltip("Force to apply when moving")]
    public float moveForce = 2f;

    [Tooltip("Speed to pitch up or down")]
    public float pitchSpeed = 100f;

    [Tooltip("Speed to rotate around the up axis")]
    public float yawSpeed = 100f;

    [Tooltip("Transform at the tip of the beak")]
    public Transform beakTip;

    [Tooltip("The agent's camera")]
    public Camera agentCamera;

    [Tooltip("Whether this si training mode or gameplay mode")]
    public bool trainingMode;

    // The rigidbody of the agent
    new private Rigidbody rigidbody; // new cause the rigidbody keyword is deprecated but we can still use it

    // The flower area that the agent is in
    private FlowerArea flowerArea;

    // The nearest flower to the agetn
    private Flower nearestFlower;

    // Allows for smoother pitch changes
    private float smoothPitchChange = 0f; // if don't smooth out, it looks jittery when a neural netowrk is controlling it

    // Allows for smoother yaw changes
    private float smoothYawChange = 0f;

    // Maximum angle that the bird can pitch up or down
    private const float MaxPitchAngle = 80f;

    // Maximum distance from the beak tip to accept nectar collision
    private const float BeakTipRadius = 0.008f;

    // Whether the agent is frozen (intentionally not flaying)
    private bool frozen = false; // useful when converting agent into an actual game
    // works well if there is an idea if it is frozen

    /// <summary>
    /// The amount of nectar the agent has obtained this episode
    /// </summary>
    public float NectarObtainted
    {
        // can have two competing hummingbirds in the game and track which one is winning and which one is losing
        get; private set;
    }

    /// <summary>
    /// Initialize the agent
    /// </summary>
    public override void Initialize()
    {
        // base.Initialize(); // if there was functionality in the base functionality we would call it
        rigidbody = GetComponent<Rigidbody>();
        flowerArea = GetComponentInParent<FlowerArea>(); // assuming the agent is a direct child of a gameobject that has a flower area script on it

        // If not training mode, no max step, play forever
        if (!trainingMode)
        {
            MaxStep = 0; // 0 is a short code for saying infinite in this case
        }
    }

    /// <summary>
    /// An agent is reset when an episode begins
    /// </summary>
    public override void OnEpisodeBegin()
    {
        // base.OnEpisodeBegin();
        if (trainingMode)
        {
            // Only reset flowers in training if there is one agent per area
            flowerArea.ResetFlowers(); // anytime the area resets, reset all flowers
        }

        // Reset nectar obtainted
        NectarObtainted = 0f;

        // Zero out velocities so that movment stops before a new episode begins (important for ML agents to reset)
        rigidbody.velocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;

        // Default to spawning in front of a flower (when training, 
        // give enough opportunities to learn when there is ideal conditions 
        // and when there is not ideal conditions)
        // split how often we start in these positions
        bool inFrontOfFlower = true;
        if (trainingMode)
        {
            // Spawn in front of flower 50% of the time during training
            inFrontOfFlower = UnityEngine.Random.value > 0.5f;
        }

        // Move the agent to a new random position
        MoveToSafeRandomPosition(inFrontOfFlower);

        // Recalculate the nearest flower now that the agent has moved
        UpdateNearestFlower(); // once you reset the agent in new place, tell it where the nearest flower is
    }

    /// <summary>
    /// Called when an action is recieved from either the player or the neural network
    /// </summary>
    /// <param name="actions">The actions to take</param>
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Recieving new action insturctions and converting it into movement in force and rotation
        // base.OnActionReceived(actions);

        // Don't take actions if frozen
        if (frozen) return;

        ActionSegment<float> vectorAction = actions.ContinuousActions;

        // Calculate movement vector
        Vector3 move = new Vector3(vectorAction[0], vectorAction[1], vectorAction[2]);

        // Add force in the direction of the move vector
        rigidbody.AddForce(move * moveForce);

        // Get the current rotation
        Vector3 rotationVector = transform.rotation.eulerAngles; // x, y, z rotation around those axis, add to those axis

        // Calculate pitch and yaw rotation
        float pitchChange = vectorAction[3]; // up and down
        float yawChange = vectorAction[4]; // left and right

        // Dont want it to go beyond the limits set

        // Calculate smooth rotation changes
        smoothPitchChange = Mathf.MoveTowards(smoothPitchChange, pitchChange, 2f * Time.fixedDeltaTime); // takes a value and moves it towards a target value, but limits how quickly it can move there
        smoothYawChange = Mathf.MoveTowards(smoothYawChange, yawChange, 2f * Time.fixedDeltaTime);

        // fixed delta time is the amount of time that has passed since the last time the physics engine was updated
        // a fixed update

        // Calculate new pitch and yaw based on smooth values
        // Clamp pitch to avoid flipping upside down
        float pitch = rotationVector.x + smoothPitchChange * Time.fixedDeltaTime * pitchSpeed;
        if (pitch > 180f) { pitch -= 360f; } // if it goes above 180, subtract 360 to bring it back down
        pitch = Mathf.Clamp(pitch, -MaxPitchAngle, MaxPitchAngle);

        float yaw = rotationVector.y + smoothYawChange * Time.fixedDeltaTime * yawSpeed;
        // No need to clamp cause we don't care if it spins in cirles from time to time

        // Apply the new rotation
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    /// <summary>
    /// Collect vector observations from the environment
    /// </summary>
    /// <param name="sensor">The vector sensor</param>
    public override void CollectObservations(VectorSensor sensor)
    {
        // Any time its called, observes details about the environment the hummingbird needs to make decisions
        // base.CollectObservations(sensor);

        // To observe rotation, vector to nearest flower, dot product if beakTip is in front of flower, 
        // dot product of beakTip pointing at the flower, relative distatnce from beakTip to flower

        // Note: there is a case where nearestFlower is set, better to observe all 0s for that microstep
        // If nearestFlower is null, observe an empty array and return early
        if (nearestFlower == null)
        {
            sensor.AddObservation(new float[10]);
            return;
        }


        // Observe the agent's local rotation (quaternion, 4 observations, add it to sensor)
        sensor.AddObservation(transform.localRotation.normalized); // normalized to make sure it is a unit quaternion

        // Get a vector from the beak tip to the nearest flower
        Vector3 toFlower = nearestFlower.FlowerCenterPosition - beakTip.position; // directly from the beak tip to the flower

        // Observe a normalized vector pointing to the nearest flower (3 observations)
        sensor.AddObservation(toFlower.normalized); // the normalized makes it between 0 and 1, purely a direction

        // Observe a dot product that indicates whether the beak tip is in front of the flower (1 observation, single float)
        // (+! means that the beak tip is directly in front of the flower, -1 means directly behind the flower)
        // to reserach how dot product can indicate these things
        sensor.AddObservation(Vector3.Dot(toFlower.normalized, -nearestFlower.FlowerUpVector.normalized));
        // when the vector to the flower and the down vector are aligned, gives a positive value

        // Observe a dot product to indicate whether the beak is pointing toward the flower (1 observation, single float)
        // (+1 means the  beak is pointing directly at the flower, -1 means directly away)
        sensor.AddObservation(Vector3.Dot(beakTip.forward.normalized, -nearestFlower.FlowerUpVector.normalized));

        // Observe the relative distance of the beak tip to the flower (1 observation, single float)
        sensor.AddObservation(toFlower.magnitude / FlowerArea.AreaDiameter);

        // 10 total observations
    }


    /// <summary>
    /// When behaviour type is set to "Heuristic Only" on the agent's Behaviour Parameters,
    /// this function will be called. Its return values will be fed into
    /// <see cref="Heuristic(in ActionBuffers)"/> instead of using the neural network 
    /// </summary>
    /// <param name = "actionsOut">An output action buffer</param>
    public override void Heuristic(in ActionBuffers actionsOutBuffer)
    {
        // base.Heuristic(actionsOut);
        // Algorithmic decision making instead of neural network decision

        // Creating a list of actions for certain circumstances received where we don't have a neural network hooked up
        // Could automatically decide where the nearest flower is and move
        // Or take in people input and move the bird around (using WASD and arrow keys)

        ActionSegment<float> actionsOut = actionsOutBuffer.ContinuousActions;

        // Create placeholders for all movement/turning
        Vector3 forward = Vector3.zero;
        Vector3 left = Vector3.zero;
        Vector3 up = Vector3.zero;
        float pitch = 0f;
        float yaw = 0f;

        // Convert keyboard inputs into movement and turning
        // All values should be between -1 and +1

        // Forward/backwards
        if (Input.GetKey(KeyCode.W)) forward = transform.forward;
        else if (Input.GetKey(KeyCode.S)) forward = -transform.forward;

        // Left/right
        if (Input.GetKey(KeyCode.A)) left = -transform.right;
        else if (Input.GetKey(KeyCode.D)) left = transform.right;

        // Up/down
        if (Input.GetKey(KeyCode.E)) up = transform.up;
        else if (Input.GetKey(KeyCode.C)) up = -transform.up;

        // Pitch up/down
        if (Input.GetKey(KeyCode.UpArrow)) pitch = -1f;
        else if (Input.GetKey(KeyCode.DownArrow)) pitch = 1f;

        // Turn left/right
        if (Input.GetKey(KeyCode.LeftArrow)) yaw = -1f;
        else if (Input.GetKey(KeyCode.RightArrow)) yaw = 1f;

        // Combine the movement vectors and normalize
        Vector3 combined = (forward + left + up).normalized;

        // Add the 3 movement values, pitch and yaw to the actionsOut array
        actionsOut[0] = combined.x;
        actionsOut[1] = combined.y;
        actionsOut[2] = combined.z;
        actionsOut[3] = pitch;
        actionsOut[4] = yaw;

        // 5 total actions
    }

    /// <summary>
    /// Prevent the agent from moving and taking actions
    /// </summary>
    public void FreezeAgent()
    {
        Debug.Assert(trainingMode == false, "Freeze/Unfreeze not supported in training");
        frozen = true; // freeze the agent
        rigidbody.Sleep(); // stop the agent from moving
    }

    /// <summary>
    /// Resume agent movement and actions
    /// </summary>
    public void UnfreezeAgent()
    {
        Debug.Assert(trainingMode == false, "Freeze/Unfreeze not supported in training");
        frozen = false; // unfreeze the agent
        rigidbody.WakeUp(); // start the agent moving
    }

    /// <summary>
    /// Move the agent to a safe random position (i.e. does not collide with anything)
    /// If in front of flower, also point the beak at the flower
    /// </summary>
    /// <param name = "inFrontOfFlower">Whether to choose a spot in front of a flower</param>
    private void MoveToSafeRandomPosition(bool inFrontOfFlower)
    {
        // Check we dont collide with anything
        bool safePositionFound = false;
        int attemptsRemaining = 100; // Prevent an infinite loop of trying to place the bird
        Vector3 potentialPosition = Vector3.zero;
        Quaternion potentialRotation = new Quaternion();

        // loop until a safe position is found or we run out of attempts
        while (!safePositionFound && attemptsRemaining > 0)
        {
            attemptsRemaining--;
            if (inFrontOfFlower)
            {
                // pick a random flower
                Flower randomFlower = flowerArea.Flowers[UnityEngine.Random.Range(0, flowerArea.Flowers.Count)]; // integer for range

                // Position 10 to 20 cm in front of the flower
                float distanceFromFlower = UnityEngine.Random.Range(.1f, .2f);
                potentialPosition = randomFlower.transform.position + randomFlower.FlowerUpVector * distanceFromFlower;

                // Point beak at flower (bird's head is center of transform) 
                // Rotate the bird around it's head so it can easily point at the flower
                Vector3 toFlower = randomFlower.FlowerCenterPosition - potentialPosition;
                potentialRotation = Quaternion.LookRotation(toFlower, Vector3.up); // makes a rotation that points the birds head directly at the flower

            }
            else
            {
                // Pick a random height from the ground
                float height = UnityEngine.Random.Range(1.2f, 2.5f);

                // Pick a random radius from the center of the area
                float radius = UnityEngine.Random.Range(2f, 7f);

                // Pick a random direction rotated around the y axis
                Quaternion direction = Quaternion.Euler(0f, UnityEngine.Random.Range(-180f, 180f), 1.0f);

                // Combine height, radius and direction to pick a potential position
                potentialPosition = flowerArea.transform.position + Vector3.up * height + direction * Vector3.forward * radius;

                // Choose and set random starting pitch and yaw
                float pitch = UnityEngine.Random.Range(-06f, 60f);
                float yaw = UnityEngine.Random.Range(-180f, 180f);
                potentialRotation = Quaternion.Euler(pitch, yaw, 0f);


            }

            // Check to see if the agent will collide with anything (at least a bubble 10cm across without crashing into anything)
            Collider[] colliders = Physics.OverlapSphere(potentialPosition, 0.05f);

            // Safe position has been found if no colliders are overlapped
            safePositionFound = colliders.Length == 0;
        }

        // Do a check that a safe position was found
        Debug.Assert(safePositionFound, "Could not find a safe position to spawn");

        // Set the positions and rotation
        transform.position = potentialPosition;
        transform.rotation = potentialRotation;
    }

    /// <summary>
    /// Update the nearest flower to the agent
    /// </summary>
    private void UpdateNearestFlower()
    {
        // Tell the hummingbird to where it is, dont want to update too often
        // Give a target until it successfully feeds from that flower

        foreach (Flower flower in flowerArea.Flowers)
        {
            if (nearestFlower == null && flower.hasNectar)
            {
                // No current nearest flower and this flower has nectar, so set to this flower
                nearestFlower = flower;
            }
            else if (flower.hasNectar)
            {
                // Calculate distance to this flower and distance to the current nearest flower
                // the beakTip is what ultimately needs to get into the flower so we use that to measure
                float distanceToFlower = Vector3.Distance(flower.transform.position, beakTip.position);
                float distanceToCurrentNearestFlower = Vector3.Distance(nearestFlower.transform.position, beakTip.position);

                // If current nearest flower is empty OR this flower is closer, update the nearest flower
                // For the situation we are feeding from a flower for a while and it is empty
                // OR if this flower is closer than the previous 
                if (!nearestFlower.hasNectar || distanceToFlower < distanceToCurrentNearestFlower)
                {
                    nearestFlower = flower;
                }
            }

        }
    }

    /// <summary>
    /// Called when the agent's collider enters a trigger collider
    /// </summary>
    /// <param name = "other">The trigger collider</param>
    private void OnTriggerEnter(Collider other)
    {
        TriggerEnterOrStay(other);
    }

    /// <summary>
    /// Called when the agent's collider stays in a trigger collider
    /// </summary>
    /// <param name = "other">The trigger collider</param>
    private void OnTriggerStay(Collider other)
    {
        TriggerEnterOrStay(other);
    }

    /// <summary>
    /// Handles when the agent's collider enters or stays in a trigger collider
    /// </summary>
    /// <param name = "collider">The trigger collider</param>
    private void TriggerEnterOrStay(Collider collider)
    {
        // Check if agent is colliding with nectar
        if (collider.CompareTag("nectar"))
        {
            Vector3 closestPointToBeakTip = collider.ClosestPoint(beakTip.position);
            // Have the bird inside the nectar, it is possible something other than the beak tip got inside the nectar collider

            // so check if the closest point from the nectar to the beak tip is exactly the same
            // Otherwise possible the head got in but not the beak

            // Check if the closest collision point is close to the beak tip
            // Note: a collision with anything but the beak tip should not count
            // Reward when the beak is inside the nectar
            if (Vector3.Distance(beakTip.position, closestPointToBeakTip) < BeakTipRadius)
            {
                // Look up the flower for this nectar collider
                Flower flower = flowerArea.GetFlowerFromNectar(collider); // Gets the flower the nectar belongs to

                // Attempt to take .01 nectar
                // Note: this is per fixed timestep, meaning it happens every 0.02 seconds, or 50x per second
                float nectarReceived = flower.Feed(0.01f); // Feed 1% of the nectar every .02 seconds

                // Keep track of nectar obtained
                NectarObtainted += nectarReceived;

                if (trainingMode)
                {
                    // Calculate reward for getting nectar
                    // Doesnt hurt to have the add rewards in not training mode, but helps to separate training vs not training mode
                    // Bonus if it is pointed directly at the flower
                    float bonus = 0.02f * Mathf.Clamp01(Vector3.Dot(transform.forward.normalized, -nearestFlower.FlowerUpVector.normalized));
                    AddReward(.01f + bonus);

                    // The rewards, ideally want a good successful run to have a total reward of +ve 1
                }

                // If flower is empty, update the nearest flower
                if (!flower.hasNectar)
                {
                    UpdateNearestFlower(); // only update nearest flower once the last flower has emptied out
                }
            }
        }
    }

    /// <summary>
    /// Called when the agent collides with something solid
    /// </summary>
    /// <param name="collision">The collision info</param>
    private void OnCollisionEnter(Collision collision)
    {
        if (trainingMode && collision.collider.CompareTag("boundary"))
        {
            // Collided with the area boundary, give a negative reward
            AddReward(-0.5f); // negative rewards to balance (eg bashing the walls)
            // But cannot have too much negative reward or it may get paralysed
        }
    }

    /// <summary>
    /// Called every frame
    /// </summary>
    private void Update()
    {
        // Do not want to use the update function for anything that affects the agent at all
        // Going to do a visualization of where the nearest flower is relative to the agent

        // Draw a line from the beak tip to the nearest flower
        if (nearestFlower != null)
        {
            Debug.DrawLine(beakTip.position, nearestFlower.FlowerCenterPosition, Color.green);
        }
    }

    /// <summary>
    /// Called every 0.02 seconds
    /// </summary>
    private void FixedUpdate()
    {
        // To fix bug where if you steal all the nectar from the agent, it never updates the nearest flower
        // Avoids scenario where nearest flower is stolen by opponent and not updated
        if (nearestFlower != null && !nearestFlower.hasNectar)
        {
            UpdateNearestFlower();
        }
    }
}

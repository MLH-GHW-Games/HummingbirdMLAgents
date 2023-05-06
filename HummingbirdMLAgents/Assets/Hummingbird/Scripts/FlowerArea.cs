using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a collection of flower plants and attached flowers
/// </summary>
public class FlowerArea : MonoBehaviour
{
    // The diameter of the area where the the agent and flowers can be 
    // used for observing relative distance from agent to flower
    public const float AreaDiameter = 20f;

    // area diameter is distance from one side of island to the other
    // if larger space to play, want higher value
    // we set to max value it can be
    // to keep the observation of distance to <1 or at most 1
    // nn works better for fractional number or percentage rather than a high number
    // if had the number go up all the way from 0-20, it may be too high for neural networks

    // The list of all flower plants in this flower area (flower plants have multiple flowers)
    private List<GameObject> flowerPlants;

    // A lookup dictionary for looking up a flower from a nectar collider
    private Dictionary<Collider, Flower> nectarFlowerDictionary;

    // when the bird collides with anything, we check if it has a nectar tag
    // then look up the flower the nectar collider belongs to

    /// <summary>
    /// The list of all flowers in the flower area
    /// </summary>
    public List<Flower> Flowers { get; private set; }

    /// <summary>
    /// Reset the flowers and flower plants
    /// </summary>
    public void ResetFlowers()
    {
        // want to set random rotations for each flower plant and reset the flowers themselves

        // Rotate each flower plant around the Y axis and subtly around X and Z
        foreach (GameObject flowerPlant in flowerPlants)
        {
            // Generate 3 rotations
            float xRotation = UnityEngine.Random.Range(-5f, 5f);
            float yRotation = UnityEngine.Random.Range(-180f, 180f);
            float zRotation = UnityEngine.Random.Range(-5f, 5f);

            flowerPlant.transform.localRotation = Quaternion.Euler(xRotation, yRotation, zRotation);

        }

        // Reset each flower
        foreach (Flower flower in Flowers)
        {
            flower.ResetFlower(); // the function defined in Flower.cs class
        }
    }

    /// <summary>
    /// Gets  the <see cref="Flower"/> that a nectar collider belongs to
    /// </summary>
    /// <param name="collider">The nectar collider</param>
    /// <returns>The matching flower</retuns>
    public Flower GetFlowerFromNectar(Collider collider)
    {
        // ask this function which flower belongs to which nectar collider
        return nectarFlowerDictionary[collider];
    }

    /// <summary>
    /// Called when the area wakes up
    /// </summary>
    private void Awake()
    {
        // Initialize variables
        flowerPlants = new List<GameObject>();
        nectarFlowerDictionary = new Dictionary<Collider, Flower>();
        Flowers = new List<Flower>();
    }

    /// <summary>
    /// Called when the game starts
    /// <summary>
    private void Start()
    {
        // Find all flowers that are children of this GameObject/Transform
        // Put in start instead of awake as the flowers may not be ready at the awake yet

        FindChildFlowers(transform); // finds all flowers that are a child of this flower area
    }

    /// <summary>
    /// Recursively finds all flowers and flower plants that are children of a parent transform
    /// <summary>
    /// <param name="parent">The parent of the children to check</param>
    private void FindChildFlowers(Transform parent)
    {
        // to know where all the flowers are relative to this flower area
        // dive down into the flower area and look for the flowers inside

        // this function is called at a high level (eg on the island)
        // two base cases, when there is not children, and one for found flower

        for (int i = 0; i < parent.childCount; i++)
        {
            // calls on some transform in the scene (could be non flower objects)
            Transform child = parent.GetChild(i);

            if (child.CompareTag("flower_plant"))
            {
                // Found a flower plant, add it to the flower plants list
                flowerPlants.Add(child.gameObject);

                // Look for flowers within the flower plant
                FindChildFlowers(child);
            }
            else
            {
                // Not a flower plant, look for a Flower component
                Flower flower = child.GetComponent<Flower>();
                if (flower != null)
                {
                    // Found a flower, add it to the Flowers list
                    Flowers.Add(flower);

                    // Add the nectar collider to the lookup dictionary
                    nectarFlowerDictionary.Add(flower.nectarCollider, flower);

                    // Note: there are no flowers that are children of other flowers
                }
                else{
                    // Flower component not found, so check children
                    FindChildFlowers(child);
                }
            }


        }



    }
}

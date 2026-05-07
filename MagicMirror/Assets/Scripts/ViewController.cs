using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CharacterSet
{
    public GameObject character;
    public GameObject background;
}

public class ViewController : MonoBehaviour
{
    public CharacterSet[] characters;
    void Start()
    {
        foreach (var character in characters)
        {
            // Make all characters invisible at the start of the game
            character.character.SetActive(false);
            character.background.SetActive(false);
        }
    }

    void Update()
    {
        // Make the character visible when the corresponding number key is pressed
        if (Input.GetKeyDown(KeyCode.Alpha0)) Toggle(0);
        if (Input.GetKeyDown(KeyCode.Alpha1)) Toggle(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) Toggle(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) Toggle(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) Toggle(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) Toggle(5);
        if (Input.GetKeyDown(KeyCode.Alpha6)) Toggle(6);
        if (Input.GetKeyDown(KeyCode.Alpha7)) Toggle(7);
        if (Input.GetKeyDown(KeyCode.Alpha8)) Toggle(8);
        if (Input.GetKeyDown(KeyCode.Alpha9)) Toggle(9);
    }

    private void Toggle(int index)
    {
        // Hide the pose detection stuff
        foreach (GameObject component in GameObject.FindGameObjectsWithTag("Default background"))
            component.SetActive(false);



        // Hide all characters, then show only the selected one
        foreach (var character in characters)
        {
            character.character.SetActive(false);
            character.background.SetActive(false);
        }

        if (index >= characters.Length)
        {
            print($"No character at index {index}");
            return;
        }

        characters[index].character.SetActive(true);
        characters[index].background.SetActive(true);
        print($"Character {index} is now visible");
    }
}
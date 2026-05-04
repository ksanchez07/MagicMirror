using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ViewController : MonoBehaviour
{
    private GameObject[] characters;

    void Start()
    {
        characters = GameObject.FindGameObjectsWithTag("character");
        foreach (GameObject character in characters)
        {
            // Make all characters invisible at the start of the game
            character.SetActive(false);
        }
    }

    void Update()
    {
        // Make the character visible when the corresponding number key is pressed
        if (Input.GetKeyDown(KeyCode.Alpha0)) ToggleCharacter(0);
        if (Input.GetKeyDown(KeyCode.Alpha1)) ToggleCharacter(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) ToggleCharacter(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) ToggleCharacter(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) ToggleCharacter(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) ToggleCharacter(5);
        if (Input.GetKeyDown(KeyCode.Alpha6)) ToggleCharacter(6);
        if (Input.GetKeyDown(KeyCode.Alpha7)) ToggleCharacter(7);
        if (Input.GetKeyDown(KeyCode.Alpha8)) ToggleCharacter(8);
        if (Input.GetKeyDown(KeyCode.Alpha9)) ToggleCharacter(9);
    }

    private void ToggleCharacter(int index)
    {
        // Hide all characters, then show only the selected one
        foreach (GameObject character in characters)
            character.SetActive(false);

        if (index >= characters.Length)
        {
            print($"No character at index {index}");
            return;
        }

        characters[index].SetActive(true);
        print($"Character {index} is now visible");
    }
}
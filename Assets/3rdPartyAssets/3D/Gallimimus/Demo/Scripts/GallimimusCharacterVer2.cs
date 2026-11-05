using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GallimimusCharacterVer2 : AnimalCharacter
{
    // Start is called before the first frame update
    /*
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }*/

    public override void Threat()
    {
        animalAnimator.SetTrigger("Threat");
    }

    public override void Eat()
    {
        animalAnimator.SetTrigger("Eat");
    }

    public override void HopStart()
    {
        animalAnimator.SetBool("IsHopping", true);
    }

    public override void HopEnd()
    {
        animalAnimator.SetBool("IsHopping", false);
    }

    public override void DiagonallyWalkStart()
    {
        animalAnimator.SetBool("IsDiagonally", true);
    }

    public override void DiagonallyWalkEnd()
    {
        animalAnimator.SetBool("IsDiagonally", false);
    }


    public override void EatStart()
    {
        animalAnimator.SetBool("IsEating", true);
    }
    public override void EatEnd()
    {
        animalAnimator.SetBool("IsEating", false);
    }

    public override void DrinkStart()
    {
        animalAnimator.SetBool("IsDrinking", true);
    }
    public override void DrinkEnd()
    {
        animalAnimator.SetBool("IsDrinking", false);
    }

    public override void SleepStart()
    {
        animalAnimator.SetBool("IsSleeping", true);
    }
    public override void SleepEnd()
    {
        animalAnimator.SetBool("IsSleeping", false);
    }




    public override void Cry()
    {
        animalAnimator.SetTrigger("Cry");
    }

}

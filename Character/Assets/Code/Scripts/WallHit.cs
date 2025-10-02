using System;
using System.Collections;
using UnityEngine;

public class WallHit : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private AudioSource audioSource;
    private Boolean hasHit = false;
    
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    
    private void OnCollisionEnter(Collision collision)
    {
        if (!hasHit)
        {
            hasHit = true;
            StartCoroutine(HitSound());
        }
       
    }

    private IEnumerator HitSound()
    {
        audioSource.Play();
        yield return new WaitForSeconds(3f);
        
    }



    private void OnCollisionExit(Collision collision)
    {
        hasHit = false;
    }

    

}

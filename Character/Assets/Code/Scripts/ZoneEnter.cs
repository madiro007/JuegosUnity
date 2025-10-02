using UnityEngine;

public class ZoneEnter : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private AudioSource audioSource;
    
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

   
    private void OnTriggerEnter(Collider other)
    {
        audioSource.Play();
        Debug.Log("Entró en zona reservada");
    }

    private void OnTriggerExit(Collider other)
    {
        audioSource.Stop();
        Debug.Log("Salió de zona reservada");
    }
}

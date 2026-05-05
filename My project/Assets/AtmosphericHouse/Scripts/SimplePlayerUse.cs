using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Atmo
{

    public class SimplePlayerUse : MonoBehaviour
    {
        public GameObject mainCamera;
        private GameObject objectClicked;
        public GameObject flashlight;
        public KeyCode OpenClose;
        public KeyCode Flashlight;

        void Start()
        {

        }

        void Update()
        {
            if (PlayerControlGate.IsInputBlocked)
                return;

            if (Input.GetKeyDown(OpenClose)) // Open and close action
            {
                RaycastCheck();
            }

            if (Input.GetKeyDown(Flashlight)) // Toggle flashlight
            {
                if (flashlight.activeSelf)
                    flashlight.SetActive(false);
                else
                    flashlight.SetActive(true);
            }
        }

        void RaycastCheck()
        {
            if (mainCamera == null)
                return;

            RaycastHit hit;

            if (Physics.Raycast(mainCamera.transform.position, mainCamera.transform.TransformDirection(Vector3.forward), out hit, 3))
            {
                // ✅ Direkt component'e erişip parametre ile çağır (oyuncu için bypassLock = false)
                SimpleOpenClose door = hit.collider.GetComponent<SimpleOpenClose>();
                //Debug.Log(hit.transform.name);
                if (door != null)
                {
                    // Debug.Log("Object with SimpleOpenClose script found");
                    door.ObjectClicked(bypassLock: false); // Oyuncu için lock kontrolü yapılır
                    
                    
                }

                else
                {
                    // Debug.Log("Object doesn't have script SimpleOpenClose attached");

                }
                // Debug.DrawRay(mainCamera.transform.position, mainCamera.transform.TransformDirection(Vector3.forward) * hit.distance, Color.yellow);
                // Debug.Log("Did Hit");
            }
            else
            {
                // Debug.DrawRay(mainCamera.transform.position, mainCamera.transform.TransformDirection(Vector3.forward) * 1000, Color.white);
                //   Debug.Log("Did not Hit");


            }

        }

    }
}
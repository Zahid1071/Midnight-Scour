using FS_Atmo;
using UnityEngine;
using System.Collections;
using UnityEngine.AI;

public class CinematicSignalFunctions : MonoBehaviour
{
    NPCController npc;
    Navigator navigator;
    public Transform gratePosition;
    public Transform destination;
    public Transform key;
    NavMeshAgent agent;
    
    // Flag to track when the cinematic move is active
    private bool isMoving = false;

    void Start()
    {
        navigator = GetComponent<Navigator>();
        npc = GetComponent<NPCController>();
        agent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        // Only check distance if the Timeline signal has started the movement
        if (isMoving && (transform.position - destination.position).magnitude < 0.1f)
        {
            // Shut off the flag immediately to prevent Update from firing this again
            isMoving = false; 
            
            npc.fsm.SetState(NPCController.AIState.Natural);
        }
    }

    // Çağrıyı normal fonksiyon yerine bu şekilde başlatın
    public void StartBasementSequence()
    {
        StartCoroutine(MovementSequenceCoroutine());
    }

    private IEnumerator MovementSequenceCoroutine()
    {
        isMoving = true;
        navigator.GoToPoint(gratePosition.position);

        // NavMeshAgent'ın hedefe varıp varmadığını kontrol eden döngü
        // 'navigator' altındaki NavMeshAgent referansına göre burayı uyarlayın:
        while (agent.pathPending || agent.remainingDistance > agent.stoppingDistance)
        {
            // Hedefe varana kadar bir sonraki kareyi bekle
            yield return null; 
        }
        
        // İlk hedefe ulaşıldı, şimdi ikinci fonksiyonu tetikle
        LeaveBasement();
        UnaprentKey();
    }

    public void LeaveBasement()
    {
        // Open the gate so Update starts looking for the destination
        isMoving = true; 
        
        navigator.GoToPoint(destination.position);

    }
    
    void UnaprentKey()
    {
        key.transform.SetParent(null);
        key.transform.GetComponent<Rigidbody>().useGravity = true;
        key.transform.GetComponent<Rigidbody>().isKinematic = false;
    }
}
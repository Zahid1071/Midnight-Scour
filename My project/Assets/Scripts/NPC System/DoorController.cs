// DoorController.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Atmo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshObstacle))]
    public class DoorController : MonoBehaviour
    {
        [Header("Animation (delegated)")]
        [Tooltip("Existing SimpleOpenClose on the DoorLeaf. We call ObjectClicked() to toggle.")]
        [SerializeField] private SimpleOpenClose simpleOpenClose;

        [Header("NavMesh (on DoorRoot)")]
        [SerializeField] private NavMeshObstacle carveObstacle; // Box, Carve = ON

        [Header("AI Waypoints (children of DoorRoot)")]
        public Transform prePointA; // side A
        public Transform prePointB; // side B

        [Header("Timing")]
        [Tooltip("Delay after Open starts before freeing the NavMesh.")]
        [SerializeField] private float openCarveDelay = 0.20f;
        [Tooltip("Delay after Close starts before blocking again.")]
        [SerializeField] private float closeCarveDelay = 0.35f;

        [Header("Obstacle policy")]
        [Tooltip("When open, fully disable obstacle to avoid agents hugging the door root.")]
        [SerializeField] private bool disableObstacleWhenOpen = true;
        [SerializeField] private float obstacleFailsafeDelay = 0.10f;

        [Header("State")]
        [Tooltip("Note: Lock is now handled by SimpleOpenClose. This field is kept for backward compatibility but AI ignores it.")]
        public bool isLocked = false;
        public bool IsOpen { get; private set; }

        public event Action OnOpened;
        public event Action OnClosed;

        private enum DoorState { Closed, Opening, Open, Closing }
        [SerializeField] private DoorState state = DoorState.Closed;

        private bool lastOpenFlag;

        private void Reset()
        {
            carveObstacle = GetComponent<NavMeshObstacle>();
            if (!simpleOpenClose) simpleOpenClose = GetComponentInChildren<SimpleOpenClose>();
        }

        private void Awake()
        {
            if (!carveObstacle) carveObstacle = GetComponent<NavMeshObstacle>();
            if (carveObstacle)
            {
                carveObstacle.carving = true;
                carveObstacle.enabled = true;
            }

            if (simpleOpenClose != null)
            {
                lastOpenFlag = simpleOpenClose.objectOpen;
                IsOpen = lastOpenFlag;
                state = IsOpen ? DoorState.Open : DoorState.Closed;
            }
            else
            {
                lastOpenFlag = false;
                IsOpen = false;
                state = DoorState.Closed;
            }

            ApplyCarveImmediate(IsOpen);
        }

        private void Update()
        {
            if (simpleOpenClose == null) return;

            if (simpleOpenClose.objectOpen != lastOpenFlag)
            {
                lastOpenFlag = simpleOpenClose.objectOpen;

                StopAllCoroutines();
                if (lastOpenFlag)
                    StartCoroutine(OpenRoutine(null, triggerAnim: false));
                else
                    StartCoroutine(CloseRoutine(null, triggerAnim: false));
            }
        }

        // ========= Public API =========

        public void SetLocked(bool locked) => isLocked = locked;

        public void RequestOpen(Action<bool> onDone = null)
        {
            // ✅ AI için lock kontrolü yok - AI her zaman kapıyı açabilir
            if (state == DoorState.Open || state == DoorState.Opening) { onDone?.Invoke(true); return; }

            StopAllCoroutines();
            StartCoroutine(OpenRoutine(onDone, triggerAnim: true));
        }

        public void RequestClose(Action onDone = null)
        {
            if (state == DoorState.Closed || state == DoorState.Closing) { onDone?.Invoke(); return; }

            StopAllCoroutines();
            StartCoroutine(CloseRoutine(onDone, triggerAnim: true));
        }

        public void ForceOpenInstant()
        {
            StopAllCoroutines();

            if (simpleOpenClose && !simpleOpenClose.objectOpen)
                simpleOpenClose.ObjectClicked();

            ApplyCarveImmediate(true);

            IsOpen = true;
            state = DoorState.Open;
            OnOpened?.Invoke();
        }

        public void ForceCloseInstant()
        {
            StopAllCoroutines();

            if (simpleOpenClose && simpleOpenClose.objectOpen)
                simpleOpenClose.ObjectClicked();

            ApplyCarveImmediate(false);

            IsOpen = false;
            state = DoorState.Closed;
            OnClosed?.Invoke();
        }

        // ========= Internals =========

        private IEnumerator OpenRoutine(Action<bool> onDone, bool triggerAnim)
        {
            state = DoorState.Opening;

            if (triggerAnim && simpleOpenClose && !simpleOpenClose.objectOpen)
                simpleOpenClose.ObjectClicked(bypassLock: true); // ✅ AI için lock bypass

            yield return new WaitForSeconds(openCarveDelay);

            SetCarve(false);

            if (disableObstacleWhenOpen && carveObstacle)
            {
                if (obstacleFailsafeDelay > 0f)
                    yield return new WaitForSeconds(obstacleFailsafeDelay);
                carveObstacle.enabled = false;
            }

            IsOpen = true;
            state = DoorState.Open;
            OnOpened?.Invoke();
            onDone?.Invoke(true);
        }

        private IEnumerator CloseRoutine(Action onDone, bool triggerAnim)
        {
            state = DoorState.Closing;

            if (carveObstacle) carveObstacle.enabled = true;

            if (triggerAnim && simpleOpenClose && simpleOpenClose.objectOpen)
                simpleOpenClose.ObjectClicked();

            yield return new WaitForSeconds(closeCarveDelay);

            SetCarve(true);

            IsOpen = false;
            state = DoorState.Closed;
            OnClosed?.Invoke();
            onDone?.Invoke();
        }

        private void ApplyCarveImmediate(bool openNow)
        {
            if (!carveObstacle) return;

            if (!openNow)
            {
                carveObstacle.enabled = true;
                carveObstacle.carving = true;
            }
            else
            {
                carveObstacle.carving = false;
                if (disableObstacleWhenOpen) carveObstacle.enabled = false;
            }
        }

        private void SetCarve(bool enabled)
        {
            if (!carveObstacle) return;

            if (!carveObstacle.enabled) carveObstacle.enabled = true;
            carveObstacle.carving = enabled;
        }
    }
}

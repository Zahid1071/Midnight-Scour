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

        [Header("AI")]
        [Tooltip("If enabled, NPCs close this door after passing through it.")]
        [SerializeField] private bool aiCloseAfterPassing = true;
        public bool AICloseAfterPassing => aiCloseAfterPassing;

        [Header("State")]
        public bool isLocked = false;
        public bool IsOpen { get; private set; }

        public event Action OnOpened;
        public event Action OnClosed;

        private enum DoorState { Closed, Opening, Open, Closing }
        [SerializeField] private DoorState state = DoorState.Closed;

        private bool lastOpenFlag;
        // True while RequestOpen/RequestClose AI routines run. Player toggles sync carve only —
        // StopAllCoroutines here would cancel the NPC onDone and break door transit.
        private bool _requestInProgress;

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

                // Player flipped the leaf while an AI request owns the carve routines —
                // sync visual/carve state only; do not cancel the NPC coroutine.
                if (_requestInProgress)
                {
                    IsOpen = lastOpenFlag;
                    state = lastOpenFlag ? DoorState.Open : DoorState.Closed;
                    ApplyCarveImmediate(lastOpenFlag);
                    if (lastOpenFlag) OnOpened?.Invoke();
                    else OnClosed?.Invoke();
                    return;
                }

                _requestInProgress = false;
                StopAllCoroutines();
                if (lastOpenFlag)
                    StartCoroutine(OpenRoutine(null, triggerAnim: false, bypassLock: false));
                else
                    StartCoroutine(CloseRoutine(null, triggerAnim: false, bypassLock: false));
            }
        }

        // ========= Public API =========

        public void SetLocked(bool locked) => isLocked = locked;

        // Backwards-compatible API (player/normal usage)
        public void RequestOpen(Action<bool> onDone = null) => RequestOpen(bypassLock: false, onDone);

        // AI/NPC can bypass lock so door still plays Open/Close (not Locked)
        public void RequestOpen(bool bypassLock, Action<bool> onDone = null)
        {
            if (isLocked && !bypassLock) { onDone?.Invoke(false); return; }
            if (state == DoorState.Open || state == DoorState.Opening) { onDone?.Invoke(true); return; }

            // StopAllCoroutines does not run finally — clear the flag explicitly.
            _requestInProgress = false;
            StopAllCoroutines();
            StartCoroutine(OpenRoutine(onDone, triggerAnim: true, bypassLock: bypassLock));
        }

        public void RequestClose(Action onDone = null) => RequestClose(bypassLock: false, onDone);

        public void RequestClose(bool bypassLock, Action onDone = null)
        {
            if (state == DoorState.Closed || state == DoorState.Closing) { onDone?.Invoke(); return; }

            _requestInProgress = false;
            StopAllCoroutines();
            StartCoroutine(CloseRoutine(onDone, triggerAnim: true, bypassLock: bypassLock));
        }

        public void ForceOpenInstant()
        {
            _requestInProgress = false;
            StopAllCoroutines();

            if (simpleOpenClose && !simpleOpenClose.objectOpen)
                simpleOpenClose.ObjectClicked(bypassLock: true);

            lastOpenFlag = true;
            ApplyCarveImmediate(true);

            IsOpen = true;
            state = DoorState.Open;
            OnOpened?.Invoke();
        }

        public void ForceCloseInstant()
        {
            _requestInProgress = false;
            StopAllCoroutines();

            if (simpleOpenClose && simpleOpenClose.objectOpen)
                simpleOpenClose.ObjectClicked(bypassLock: true);

            lastOpenFlag = false;
            ApplyCarveImmediate(false);

            IsOpen = false;
            state = DoorState.Closed;
            OnClosed?.Invoke();
        }

        // ========= Internals =========

        private IEnumerator OpenRoutine(Action<bool> onDone, bool triggerAnim, bool bypassLock)
        {
            _requestInProgress = triggerAnim;
            state = DoorState.Opening;

            try
            {
                if (triggerAnim && simpleOpenClose && !simpleOpenClose.objectOpen)
                    simpleOpenClose.ObjectClicked(bypassLock: bypassLock);

                yield return new WaitForSeconds(openCarveDelay);

                // Player may have slammed the leaf closed during the wait — re-open for AI.
                if (bypassLock && simpleOpenClose && !simpleOpenClose.objectOpen)
                    simpleOpenClose.ObjectClicked(bypassLock: true);

                SetCarve(false);

                if (disableObstacleWhenOpen && carveObstacle)
                {
                    if (obstacleFailsafeDelay > 0f)
                        yield return new WaitForSeconds(obstacleFailsafeDelay);
                    carveObstacle.enabled = false;
                }

                IsOpen = true;
                lastOpenFlag = simpleOpenClose == null || simpleOpenClose.objectOpen;
                state = DoorState.Open;
                OnOpened?.Invoke();
                onDone?.Invoke(true);
            }
            finally
            {
                _requestInProgress = false;
            }
        }

        private IEnumerator CloseRoutine(Action onDone, bool triggerAnim, bool bypassLock)
        {
            _requestInProgress = triggerAnim;
            state = DoorState.Closing;

            try
            {
                if (carveObstacle) carveObstacle.enabled = true;

                if (triggerAnim && simpleOpenClose && simpleOpenClose.objectOpen)
                    simpleOpenClose.ObjectClicked(bypassLock: bypassLock);

                yield return new WaitForSeconds(closeCarveDelay);

                if (bypassLock && simpleOpenClose && simpleOpenClose.objectOpen)
                    simpleOpenClose.ObjectClicked(bypassLock: true);

                SetCarve(true);

                IsOpen = false;
                lastOpenFlag = simpleOpenClose != null && simpleOpenClose.objectOpen;
                state = DoorState.Closed;
                OnClosed?.Invoke();
                onDone?.Invoke();
            }
            finally
            {
                _requestInProgress = false;
            }
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

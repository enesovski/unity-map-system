using Artifika.Map.Core;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map.World
{
    [DisallowMultipleComponent]
    public abstract class MapMarkerAnchorBase : MonoBehaviour
    {
        [Title("Base Settings")]
        [SerializeField] protected bool registerOnEnable = true;

        protected MapSystemContext Context { get; private set; }
        protected bool IsRegistered { get; private set; }
        private bool registrationDesired;

        public abstract MarkerType Type { get; }

        protected virtual void Awake()
        {
            registrationDesired = registerOnEnable;
        }

        protected virtual void OnEnable()
        {
            if (registrationDesired)
                TryRegisterWhenReady();
        }

        protected virtual void OnDisable()
        {
            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnShutdown -= OnMapShutdown;

            UnregisterIfNeeded();
        }

        protected virtual void OnDestroy()
        {
            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnShutdown -= OnMapShutdown;

            UnregisterIfNeeded();
        }

        private void TryRegisterWhenReady()
        {
            if (!registrationDesired || !isActiveAndEnabled)
                return;

            if (MapSystemContext.TryGetInitialized(out var context))
            {
                BindAndRegister(context);
                return;
            }

            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnInitialized += OnMapInitialized;
        }

        private void OnMapInitialized(MapSystemContext context)
        {
            if (!registrationDesired || !isActiveAndEnabled)
                return;

            BindAndRegister(context);
        }

        private void OnMapShutdown()
        {
            UnregisterIfNeeded();
            Context = null;

            if (registrationDesired && isActiveAndEnabled)
            {
                MapSystemContext.OnInitialized -= OnMapInitialized;
                MapSystemContext.OnInitialized += OnMapInitialized;
            }
        }

        private void BindAndRegister(MapSystemContext context)
        {
            if (!registrationDesired || !isActiveAndEnabled || IsRegistered)
                return;

            Context = context;
            MapSystemContext.OnInitialized -= OnMapInitialized;
            MapSystemContext.OnShutdown -= OnMapShutdown;
            MapSystemContext.OnShutdown += OnMapShutdown;

            Register();
            IsRegistered = true;
        }

        protected void UnregisterIfNeeded()
        {
            if (!IsRegistered)
                return;

            Unregister();
            IsRegistered = false;
        }

        public abstract void Register();
        public abstract void Unregister();

        /// <summary>
        /// Sets whether this marker should exist on the map. Registration waits
        /// for MapSystemContext when necessary and survives map reinitialization.
        /// </summary>
        public void SetRegistrationDesired(bool desired)
        {
            registrationDesired = desired;

            if (!desired)
            {
                MapSystemContext.OnInitialized -= OnMapInitialized;
                UnregisterIfNeeded();
                return;
            }

            if (isActiveAndEnabled)
                TryRegisterWhenReady();
        }
    }
}

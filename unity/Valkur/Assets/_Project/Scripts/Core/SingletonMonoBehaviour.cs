using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Generic singleton base class for MonoBehaviours.
    /// Provides consistent lifecycle management: duplicate guard, optional DontDestroyOnLoad,
    /// and guaranteed Instance cleanup on destroy.
    /// 
    /// Usage: public class MyManager : SingletonMonoBehaviour&lt;MyManager&gt; { }
    /// Override Persist to true if the singleton should survive scene loads.
    /// </summary>
    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : SingletonMonoBehaviour<T>
    {
        private static T _instance;

        public static T Instance => _instance;
        public static bool HasInstance => _instance != null;

        /// <summary>
        /// Override to true if this singleton should persist across scene loads.
        /// </summary>
        protected virtual bool Persist => false;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[Singleton] Duplicate {typeof(T).Name} destroyed on {gameObject.name}.");
                Destroy(gameObject);
                return;
            }

            _instance = (T)this;

            if (Persist)
                DontDestroyOnLoad(gameObject);

            OnSingletonAwake();
        }

        /// <summary>
        /// Called after singleton is established. Override instead of Awake.
        /// </summary>
        protected virtual void OnSingletonAwake() { }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}

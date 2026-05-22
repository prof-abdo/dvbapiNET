using System;
using System.Diagnostics;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Gère la stratégie de reconnexion avec backoff exponentiel.
    /// Implémente une reconnection automatique intelligente vers le serveur Oscam.
    /// </summary>
    public class ReconnectionStrategy
    {
        private readonly int _minDelayMs;
        private readonly int _maxDelayMs;
        private readonly double _backoffMultiplier;
        private int _currentDelayMs;
        private int _attemptCount;
        private readonly Stopwatch _lastAttemptTimer;

        /// <summary>
        /// Initialise la stratégie de reconnection.
        /// </summary>
        /// <param name="minDelayMs">Délai minimal en millisecondes (défaut: 1000ms)</param>
        /// <param name="maxDelayMs">Délai maximal en millisecondes (défaut: 32000ms)</param>
        /// <param name="backoffMultiplier">Multiplicateur de backoff (défaut: 2.0)</param>
        public ReconnectionStrategy(int minDelayMs = 1000, int maxDelayMs = 32000, double backoffMultiplier = 2.0)
        {
            _minDelayMs = minDelayMs;
            _maxDelayMs = maxDelayMs;
            _backoffMultiplier = backoffMultiplier;
            _currentDelayMs = minDelayMs;
            _attemptCount = 0;
            _lastAttemptTimer = new Stopwatch();
        }

        /// <summary>
        /// Retourne true si suffisamment de temps s'est écoulé pour tenter une reconnexion.
        /// </summary>
        public bool CanRetryNow => !_lastAttemptTimer.IsRunning || _lastAttemptTimer.ElapsedMilliseconds >= _currentDelayMs;

        /// <summary>
        /// Nombre de tentatives échouées consécutives.
        /// </summary>
        public int AttemptCount => _attemptCount;

        /// <summary>
        /// Délai actuel avant prochaine tentative (en ms).
        /// </summary>
        public int CurrentDelayMs => _currentDelayMs;

        /// <summary>
        /// Enregistre une tentative de reconnexion échouée. Augmente le backoff.
        /// </summary>
        public void OnConnectionFailed()
        {
            _attemptCount++;
            _lastAttemptTimer.Restart();

            // Augmenter le délai de manière exponentielle
            int newDelay = (int)(_currentDelayMs * _backoffMultiplier);
            _currentDelayMs = Math.Min(newDelay, _maxDelayMs);
        }

        /// <summary>
        /// Enregistre une connexion réussie. Réinitialise le backoff.
        /// </summary>
        public void OnConnectionSuccess()
        {
            _lastAttemptTimer.Stop();
            _currentDelayMs = _minDelayMs;
            _attemptCount = 0;
        }

        /// <summary>
        /// Réinitialise la stratégie (reconnexion forcée).
        /// </summary>
        public void Reset()
        {
            _lastAttemptTimer.Stop();
            _currentDelayMs = _minDelayMs;
            _attemptCount = 0;
        }

        /// <summary>
        /// Retourne une description texte de l'état de la stratégie.
        /// </summary>
        public override string ToString()
        {
            return $"Attempt={_attemptCount}, NextRetryIn={Math.Max(0, _currentDelayMs - (_lastAttemptTimer.IsRunning ? (int)_lastAttemptTimer.ElapsedMilliseconds : 0))}ms";
        }
    }
}

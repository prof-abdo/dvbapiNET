using System;
using System.Collections.Generic;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Queue thread-safe pour les commandes en attente pendant une déconnexion.
    /// Permet de buffer les messages et les renvoyer lors de la reconnexion.
    /// </summary>
    public class PendingCommandQueue
    {
        private readonly Queue<PendingCommand> _queue = new Queue<PendingCommand>();
        private readonly int _maxSize;

        public struct PendingCommand
        {
            public byte[] Data;
            public bool Force; // true si doit être envoyé sans ServerInfo
            public long Timestamp; // Timestamp de création

            public PendingCommand(byte[] data, bool force = false)
            {
                Data = data;
                Force = force;
                Timestamp = DateTime.UtcNow.Ticks;
            }
        }

        public int Count => _queue.Count;
        public bool HasPending => _queue.Count > 0;

        /// <summary>
        /// Initialise la queue avec une taille maximale.
        /// </summary>
        /// <param name="maxSize">Nombre maximal de commandes à buffer (défaut: 100)</param>
        public PendingCommandQueue(int maxSize = 100)
        {
            _maxSize = maxSize;
        }

        /// <summary>
        /// Ajoute une commande à la queue.
        /// </summary>
        /// <returns>true si ajoutée, false si la queue est pleine</returns>
        public bool Enqueue(byte[] data, bool force = false)
        {
            lock (_queue)
            {
                if (_queue.Count >= _maxSize)
                    return false;

                _queue.Enqueue(new PendingCommand(data, force));
                return true;
            }
        }

        /// <summary>
        /// Récupère et vide toutes les commandes en attente.
        /// </summary>
        public PendingCommand[] DequeueAll()
        {
            lock (_queue)
            {
                if (_queue.Count == 0)
                    return new PendingCommand[0];

                PendingCommand[] result = _queue.ToArray();
                _queue.Clear();
                return result;
            }
        }

        /// <summary>
        /// Vide la queue.
        /// </summary>
        public void Clear()
        {
            lock (_queue)
            {
                _queue.Clear();
            }
        }
    }
}

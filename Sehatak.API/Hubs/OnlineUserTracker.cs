namespace Sehatak.API.Hubs
{
    /// <summary>Counts the open connections of each user (a user can have the app open on several devices).</summary>
    public class OnlineUserTracker
    {
        private readonly Dictionary<string, int> _connections = new();
        private readonly object _lock = new();

        /// <returns>true when this is the user's first connection (he just came online).</returns>
        public bool UserConnected(string userId)
        {
            lock (_lock)
            {
                _connections.TryGetValue(userId, out var count);
                _connections[userId] = count + 1;
                return count == 0;
            }
        }

        /// <returns>true when this was the user's last connection (he just went offline).</returns>
        public bool UserDisconnected(string userId)
        {
            lock (_lock)
            {
                if (!_connections.TryGetValue(userId, out var count))
                    return false;                       // never connected (or already gone): nothing to change

                if (count <= 1)
                {
                    _connections.Remove(userId);
                    return true;
                }

                _connections[userId] = count - 1;
                return false;
            }
        }

        public List<string> GetOnlineUsers()
        {
            lock (_lock) return _connections.Keys.ToList();
        }
    }
}

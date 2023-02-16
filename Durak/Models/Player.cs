namespace Durak.Models
{
    public class Player
    {
        public string Name { get; set; }
        public List<string> Connections { get; set; }
        public string UserID { get; set; }

        public Player(string name, string id, string userID)
        {
            Name = name;
            Connections = new List<string>();
            Connections.Add(id);
            UserID = UserID;
        }
    }
}

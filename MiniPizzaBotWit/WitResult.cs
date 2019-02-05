namespace MiniPizzaBot
{
    public class WitResult
    {
        public string _text { get; set; }
        public Entities entities { get; set; }
        public string msg_id { get; set; }
    }

    public class Entities
    {
        public Number[] number { get; set; }
        public Pizza_Name[] pizza_name { get; set; }
        public Intent[] intent { get; set; }
    }

    public class Number
    {
        public int confidence { get; set; }
        public int value { get; set; }
        public string type { get; set; }
    }

    public class Pizza_Name
    {
        public float confidence { get; set; }
        public string value { get; set; }
        public string type { get; set; }
    }

    public class Intent
    {
        public float confidence { get; set; }
        public string value { get; set; }
    }
}

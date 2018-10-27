using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EmergencyServicesBot
{

    public class Intent
    {
        public string query { get; set; }
        public Topscoringintent topScoringIntent { get; set; }
        public Entity[] entities { get; set; }
    }


    public class Topscoringintent
    {
        public string intent { get; set; }
        public float score { get; set; }
    }

    public class Entity
    {
        public string entity { get; set; }
        public string type { get; set; }
        public int startIndex { get; set; }
        public int endIndex { get; set; }
        public Resolution resolution { get; set; }
    }

    public class Resolution
    {
        public Value[] values { get; set; }
    }

    public class Value
    {
        public string timex { get; set; }
        public string type { get; set; }
        public string value { get; set; }
    }





}
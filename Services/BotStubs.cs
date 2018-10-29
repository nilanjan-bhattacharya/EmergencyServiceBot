using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EmergencyServicesBot.Services
{
    public class BotStubs
    {
        public static string ValidateId(string id)
        {
            return String.Format("Id {0} found", id);
        }
        public static int Register(int id, DateTime date)
        {
            return 100;
        }
        public static string AuthenticateUser(int id)
        {
            if (id == 69134)
            {
                return "Nilanjan";
            }
            else if (id == 69135)
            {
                return "Satish";
            }
            else
            {
                return null;
            }
        }

        public static List<DateTime> GetDates()
        {
            return new List<DateTime>() { DateTime.Now, DateTime.Now.AddDays(1), DateTime.Now.AddDays(2), DateTime.Now.AddDays(3), DateTime.Now.AddDays(4) };
        }
        public static DateTime? GetRegistration(int callerId)
        {
            return null;
        }

        public static DateTime? GetUserRegistrationDate(int? callerId)
        {
            if (callerId == 69135)
            {
                return DateTime.Now;
            }
            else
            {
                return null;
            }
        }

        public static bool CancelRegistration(int callerId) {
            return true;
        }
    }
}
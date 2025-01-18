using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ChatApplication.Helper
{
    public static class NetworkingHelper
    {
        public static readonly IPAddress SERVER_IPADDRESS = IPAddress.Parse("192.168.1.21");
        public static readonly int SERVER_PORT = 13000;
        public static readonly int SERVER_BACKLOG = 10;
        public static readonly string SERVER_WELCOME_TEXT = "Welcome to Chat Room!";
        public static readonly string CHAT_PROMPT_EXIT = "<EXIT>";
    }
}

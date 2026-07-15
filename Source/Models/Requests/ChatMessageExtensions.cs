namespace RimSynapse
{
    public class ChatToolCall
    {
        public string id;
        public string type = "function";
        public ChatFunctionCall function;
    }

    public class ChatFunctionCall
    {
        public string name;
        public string arguments;
    }

    public class GameToolDefinition
    {
        public string name;
        public string description;
        public object parameters; // JSON schema JObject/Dictionary
    }
}

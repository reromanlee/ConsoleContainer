using UnityEngine.UIElements;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// One recycled row of the viewer's message list: type icon, time and the message's first line.
    /// Built once per visible slot and re-bound as the list scrolls, so it holds no per-message
    /// state beyond what it currently shows.
    /// </summary>
    internal sealed class MessageRow : VisualElement
    {
        internal const string ClassName = "message-row";

        private const string IconClass = ClassName + "__icon";
        private const string TimeClass = ClassName + "__time";
        private const string TextClass = ClassName + "__text";
        private const string WarningClass = ClassName + "--warning";
        private const string ErrorClass = ClassName + "--error";

        private readonly Label time;
        private readonly Label text;
        private ConsoleMessageType shownType = ConsoleMessageType.Text;

        public MessageRow()
        {
            AddToClassList(ClassName);
            pickingMode = PickingMode.Position;

            VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(IconClass);
            Add(icon);

            time = CreateLabel(TimeClass);
            text = CreateLabel(TextClass);
        }

        public void Bind(ConsoleMessage message)
        {
            time.text = message.TimeText;
            text.text = MessageDetails.RowText(message);
            SetType(message.Type);
        }

        private Label CreateLabel(string className)
        {
            // Created in code, so escape sequences in logged text (Windows paths especially) are
            // shown literally instead of being turned into line breaks and tabs.
            Label label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            Add(label);
            return label;
        }

        private void SetType(ConsoleMessageType type)
        {
            if (type == shownType)
            {
                return;
            }

            EnableInClassList(WarningClass, type == ConsoleMessageType.Warning);
            EnableInClassList(ErrorClass, type == ConsoleMessageType.Error);
            shownType = type;
        }
    }
}

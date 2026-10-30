using UnityEngine.UIElements;

namespace reromanlee.ConsoleContainer.Editor
{
    /// <summary>
    /// One recycled row of the viewer's message list: type icon, time, the message's first line
    /// and, in the merged view, the name of its instance. Built once per visible slot and re-bound
    /// as the list scrolls, so it holds no per-message state beyond what it currently shows.
    /// </summary>
    internal sealed class MessageRow : VisualElement
    {
        internal const string ClassName = "message-row";
        internal const string InstanceClass = ClassName + "__instance";
        internal const string InstanceNameClass = ClassName + "__instance-name";

        private const string IconClass = ClassName + "__icon";
        private const string TimeClass = ClassName + "__time";
        private const string TextClass = ClassName + "__text";
        private const string WarningClass = ClassName + "--warning";
        private const string ErrorClass = ClassName + "--error";

        private readonly Label time;
        private readonly Label text;
        private readonly VisualElement instance;
        private readonly Label instanceName;
        private ConsoleMessageType shownType = ConsoleMessageType.Text;
        private bool isInstanceShown = true;

        public MessageRow()
        {
            AddToClassList(ClassName);
            pickingMode = PickingMode.Position;

            VisualElement icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(IconClass);
            Add(icon);

            time = CreateLabel(this, TimeClass);
            text = CreateLabel(this, TextClass);

            // The width cap sits on a plain container: Unity's text measurement applies a
            // percentage max-width set on a label a second time, cutting names far shorter.
            instance = new VisualElement { pickingMode = PickingMode.Ignore };
            instance.AddToClassList(InstanceClass);
            Add(instance);
            instanceName = CreateLabel(instance, InstanceNameClass);
        }

        /// <param name="message">The message to show.</param>
        /// <param name="instanceName">Shown at the row's end; null hides it.</param>
        public void Bind(ConsoleMessage message, string instanceName)
        {
            time.text = message.TimeText;
            text.text = MessageDetails.RowText(message);
            SetType(message.Type);
            SetInstance(instanceName);
        }

        private static Label CreateLabel(VisualElement parent, string className)
        {
            // Created in code, so escape sequences in logged text (Windows paths especially) are
            // shown literally instead of being turned into line breaks and tabs.
            Label label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
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

        private void SetInstance(string name)
        {
            bool isShown = name != null;
            if (isShown != isInstanceShown)
            {
                instance.style.display = isShown ? DisplayStyle.Flex : DisplayStyle.None;
                isInstanceShown = isShown;
            }

            if (isShown)
            {
                instanceName.text = name;
            }
        }
    }
}

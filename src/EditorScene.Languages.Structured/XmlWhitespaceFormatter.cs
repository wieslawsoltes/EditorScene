using System.Xml.Linq;

namespace EditorScene.Languages.Structured;

/// <summary>Conservative formatting: preserve existing text nodes; indent only element-only containers.</summary>
internal static class XmlWhitespaceFormatter
{
    internal static void Apply(XDocument document, string newline, CancellationToken cancellationToken)
    {
        if (document.Root is not { } root) return;
        var stack = new Stack<(XElement Element, int Depth, bool Preserve)>(); stack.Push((root, 0, false));
        var indents = new Dictionary<int, string>(); long addedCharacters = 0;
        string Indent(int depth)
        {
            if (!indents.TryGetValue(depth, out var value)) indents.Add(depth, value = newline + new string(' ', depth * 4));
            return value;
        }
        while (stack.TryPop(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frame.Depth > 256) throw new InvalidOperationException("XML formatting is limited to 256 nesting levels; the document was not changed.");
            var preserve = frame.Element.Attribute(XNamespace.Xml + "space")?.Value switch
            { "preserve" => true, "default" => false, _ => frame.Preserve };
            var children = frame.Element.Elements().ToArray();
            if (!preserve && children.Length > 0 && !frame.Element.Nodes().OfType<XText>().Any())
            {
                var nodes = frame.Element.Nodes().ToArray();
                var before = Indent(frame.Depth + 1); var after = Indent(frame.Depth);
                addedCharacters += (long)before.Length * nodes.Length + after.Length;
                if (addedCharacters > 8 * 1024 * 1024) throw new InvalidOperationException("XML formatting would add more than 8 MiB of indentation; the document was not changed.");
                foreach (var node in nodes) node.AddBeforeSelf(new XText(before));
                frame.Element.Add(new XText(after));
            }
            for (var i = children.Length - 1; i >= 0; i--) stack.Push((children[i], frame.Depth + 1, preserve));
        }
    }
}

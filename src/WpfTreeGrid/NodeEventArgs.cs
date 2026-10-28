using System.ComponentModel;

namespace WpfTreeGrid;


public sealed class NodeExpandingEventArgs(TreeNode node) : CancelEventArgs
{
    public TreeNode Node { get; } = node;
}

public sealed class NodeExpandedEventArgs(TreeNode node) : EventArgs
{
    public TreeNode Node { get; } = node;
}

public sealed class NodeCollapsingEventArgs(TreeNode node) : CancelEventArgs
{
    public TreeNode Node { get; } = node;
}

public sealed class NodeCollapsedEventArgs(TreeNode node) : EventArgs
{
    public TreeNode Node { get; } = node;
}

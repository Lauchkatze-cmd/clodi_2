using System;
using System.Collections.Generic;

/// <summary>
/// A small Behavior Tree framework.
/// Every node returns Success, Failure or Running when it is ticked.
/// </summary>
public enum BTState
{
    Success,
    Failure,
    Running
}

/// <summary>Base class for all Behavior Tree nodes.</summary>
public abstract class BTNode
{
    public string Name { get; }

    protected BTNode(string name)
    {
        Name = name;
    }

    public abstract BTState Tick();
}

/// <summary>
/// Selector ("OR"): tries its children from left to right
/// and stops at the first child that does NOT fail.
/// </summary>
public class BTSelector : BTNode
{
    private readonly List<BTNode> children;

    public BTSelector(string name, params BTNode[] children) : base(name)
    {
        this.children = new List<BTNode>(children);
    }

    public override BTState Tick()
    {
        foreach (BTNode child in children)
        {
            BTState state = child.Tick();
            if (state != BTState.Failure) return state;
        }
        return BTState.Failure;
    }
}

/// <summary>
/// Sequence ("AND"): runs its children from left to right
/// and stops at the first child that does NOT succeed.
/// </summary>
public class BTSequence : BTNode
{
    private readonly List<BTNode> children;

    public BTSequence(string name, params BTNode[] children) : base(name)
    {
        this.children = new List<BTNode>(children);
    }

    public override BTState Tick()
    {
        foreach (BTNode child in children)
        {
            BTState state = child.Tick();
            if (state != BTState.Success) return state;
        }
        return BTState.Success;
    }
}

/// <summary>Condition leaf: Success if the check is true, otherwise Failure.</summary>
public class BTCondition : BTNode
{
    private readonly Func<bool> check;

    public BTCondition(string name, Func<bool> check) : base(name)
    {
        this.check = check;
    }

    public override BTState Tick()
    {
        return check() ? BTState.Success : BTState.Failure;
    }
}

/// <summary>Action leaf: runs a method that returns its own state.</summary>
public class BTAction : BTNode
{
    private readonly Func<BTState> action;

    public BTAction(string name, Func<BTState> action) : base(name)
    {
        this.action = action;
    }

    public override BTState Tick()
    {
        return action();
    }
}

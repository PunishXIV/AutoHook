using System;

namespace AutoHook.Classes;

public abstract class BaseOption
{
    public Guid UniqueId { get; set; } = Guid.NewGuid();
    
    public abstract void DrawOptions();
}
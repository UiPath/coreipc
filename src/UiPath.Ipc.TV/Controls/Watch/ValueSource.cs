namespace UiPath.Ipc.TV;

public abstract partial class ValueSource
{
    public abstract string GetName();
    public abstract object? GetValue();
    public abstract Type GetDeclaredType();
    public abstract string GetImageKey();

    public virtual bool IsReference(out string? id)
    {
        id = null;
        return false;
    }
}


[System.Serializable]
public class ClassDataBack
{
    public string name;
    public string Description;
    public string id;
    public int downloads;


    public override bool Equals(object obj)
    {
        return obj is ClassDataBack other && id == other.id;
    }

    public override int GetHashCode()
    {
        return id != null ? id.GetHashCode() : 0;
    }
}
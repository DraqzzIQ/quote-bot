public struct Quote
{
    public string Name { get; set; }
    public string Content { get; set; }
    public string Culprit { get; set; }
    public string FilePath { get; set; }
    public int Upvotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime RecordedAt { get; set; }

    public override bool Equals(object obj)
    {
        if (obj is not Quote other)
            return false;

        return Name == other.Name && Content == other.Content && Culprit == other.Culprit && FilePath == other.FilePath && Upvotes == other.Upvotes && CreatedAt == other.CreatedAt && RecordedAt == other.RecordedAt;
    }
}

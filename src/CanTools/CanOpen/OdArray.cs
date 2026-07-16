namespace CanTools.CanOpen;

/// <summary>
/// An object dictionary array. Members not explicitly defined are synthesized on
/// access from the subindex-1 template, like python-canopen does: the same type and
/// attributes with the name suffixed by the subindex in lowercase hex.
/// </summary>
public sealed class OdArray : OdComposite
{
    public OdArray(int index, string name)
        : base(index, name)
    {
    }

    public override OdVariable this[int subindex]
    {
        get
        {
            if (TryGetMember(subindex, out var member))
            {
                return member!;
            }

            if (subindex is >= 1 and <= 255 && TryGetMember(1, out var template))
            {
                return Synthesize(template!, subindex);
            }

            throw new KeyNotFoundException(
                $"Array 0x{Index:X4} has no member at subindex {subindex}.");
        }
    }

    private OdVariable Synthesize(OdVariable template, int subindex) =>
        new(Index, subindex, $"{template.Name}_{subindex:x}")
        {
            DataType = template.DataType,
            AccessType = template.AccessType,
            Minimum = template.Minimum,
            Maximum = template.Maximum,
            Default = template.Default,
            Factor = template.Factor,
            Unit = template.Unit,
            Description = template.Description,
            StorageLocation = template.StorageLocation,
            CustomOptions = template.CustomOptions,
        };
}

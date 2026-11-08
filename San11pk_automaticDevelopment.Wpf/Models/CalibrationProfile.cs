namespace San11pk_automaticDevelopment.Wpf.Models;

public sealed class ClickPoint
{
    public int X { get; set; }
    public int Y { get; set; }

    public override string ToString() => $"({X}, {Y})";
}

public sealed class CalibrationProfile
{
    public string ProcessName { get; set; } = "San11WPK";
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public ClickPoint? ContextMenu { get; set; }
    public ClickPoint? DomesticMenu { get; set; }
    public ClickPoint? RecruitMenu { get; set; }
    public ClickPoint? ConfirmButton { get; set; }

    public ClickPoint? GetPoint(int step) => step switch
    {
        1 => ContextMenu,
        2 => DomesticMenu,
        3 => RecruitMenu,
        4 => ConfirmButton,
        _ => null
    };

    public void SetPoint(int step, ClickPoint point)
    {
        switch (step)
        {
            case 1: ContextMenu = point; break;
            case 2: DomesticMenu = point; break;
            case 3: RecruitMenu = point; break;
            case 4: ConfirmButton = point; break;
            default: throw new ArgumentOutOfRangeException(nameof(step));
        }
    }
}

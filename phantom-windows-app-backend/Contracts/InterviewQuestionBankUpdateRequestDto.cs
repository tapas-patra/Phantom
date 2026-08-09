namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class InterviewQuestionBankUpdateRequestDto
{
    public string InterviewName { get; set; } = string.Empty;
    public List<string> Questions { get; set; } = new();
}

namespace SuperRecruiter.Models;

public class FlareSolverrResponse
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public FlareSolverrSolution? Solution { get; set; }
}

public class FlareSolverrSolution
{
    public string Url { get; set; } = string.Empty;
    public int Status { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Response { get; set; } = string.Empty;
}

namespace EnterpriseAiGateway.Data.Models;

public class Customer
{
    public int CustomerID { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? EmailAddress { get; set; }
    public string? Phone { get; set; }
}

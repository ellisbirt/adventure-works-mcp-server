using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseAiGateway.Data.Scaffolded.Entities;

public partial class Customer
{
    public Customer()
    {
        PasswordHash = string.Empty;
        PasswordSalt = string.Empty;
    }

    [NotMapped]
    public int CustomerID
    {
        get => CustomerId;
        set => CustomerId = value;
    }
}
using System;
using System.Collections.Generic;

namespace EnterpriseAiGateway.Data.Scaffolded.Entities;

public partial class CustomerAddress
{
    public int CustomerId { get; set; }

    public int AddressId { get; set; }

    public string AddressType { get; set; } = null!;

    public Guid Rowguid { get; set; }

    public DateTime ModifiedDate { get; set; }

    public virtual Address Address { get; set; } = null!;

    public virtual Customer Customer { get; set; } = null!;
}

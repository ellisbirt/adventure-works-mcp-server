using EnterpriseAiGateway.Data;
using EnterpriseAiGateway.Data.Scaffolded.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseAiGateway.Data.Scaffolded;

// Kept in its own partial file, separate from the regenerated AdventureWorksDbContext.cs, so
// `dotnet ef dbcontext scaffold` can overwrite the generated file without deleting this policy.
public partial class AdventureWorksDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        McpEntityExposure.Annotate(
            modelBuilder.Entity<Address>(),
            McpFieldExposure.Safe,
            nameof(Address.AddressId), nameof(Address.City), nameof(Address.StateProvince),
            nameof(Address.CountryRegion), nameof(Address.ModifiedDate));
        McpEntityExposure.Annotate(
            modelBuilder.Entity<Address>(),
            McpFieldExposure.Redact,
            nameof(Address.AddressLine1), nameof(Address.AddressLine2), nameof(Address.PostalCode));

        McpEntityExposure.Annotate(
            modelBuilder.Entity<Customer>(),
            McpFieldExposure.Safe,
            nameof(Customer.CustomerId), nameof(Customer.NameStyle), nameof(Customer.SalesPerson),
            nameof(Customer.ModifiedDate));
        McpEntityExposure.Annotate(
            modelBuilder.Entity<Customer>(),
            McpFieldExposure.Redact,
            nameof(Customer.Title), nameof(Customer.FirstName), nameof(Customer.MiddleName),
            nameof(Customer.LastName), nameof(Customer.Suffix), nameof(Customer.CompanyName),
            nameof(Customer.EmailAddress), nameof(Customer.Phone));

        McpEntityExposure.Annotate(
            modelBuilder.Entity<CustomerAddress>(),
            McpFieldExposure.Safe,
            nameof(CustomerAddress.CustomerId), nameof(CustomerAddress.AddressId),
            nameof(CustomerAddress.AddressType), nameof(CustomerAddress.ModifiedDate));

        McpEntityExposure.Annotate(
            modelBuilder.Entity<Product>(),
            McpFieldExposure.Safe,
            nameof(Product.ProductId), nameof(Product.Name), nameof(Product.ProductNumber),
            nameof(Product.Color), nameof(Product.StandardCost), nameof(Product.ListPrice),
            nameof(Product.Size), nameof(Product.Weight), nameof(Product.ProductCategoryId),
            nameof(Product.ProductModelId), nameof(Product.SellStartDate), nameof(Product.SellEndDate),
            nameof(Product.DiscontinuedDate), nameof(Product.ModifiedDate));

        McpEntityExposure.Annotate(
            modelBuilder.Entity<ProductCategory>(),
            McpFieldExposure.Safe,
            nameof(ProductCategory.ProductCategoryId), nameof(ProductCategory.ParentProductCategoryId),
            nameof(ProductCategory.Name), nameof(ProductCategory.ModifiedDate));
        McpEntityExposure.Annotate(
            modelBuilder.Entity<ProductDescription>(),
            McpFieldExposure.Safe,
            nameof(ProductDescription.ProductDescriptionId), nameof(ProductDescription.Description),
            nameof(ProductDescription.ModifiedDate));
        McpEntityExposure.Annotate(
            modelBuilder.Entity<ProductModel>(),
            McpFieldExposure.Safe,
            nameof(ProductModel.ProductModelId), nameof(ProductModel.Name), nameof(ProductModel.ModifiedDate));
        McpEntityExposure.Annotate(
            modelBuilder.Entity<ProductModelProductDescription>(),
            McpFieldExposure.Safe,
            nameof(ProductModelProductDescription.ProductModelId), nameof(ProductModelProductDescription.ProductDescriptionId),
            nameof(ProductModelProductDescription.Culture), nameof(ProductModelProductDescription.ModifiedDate));

        McpEntityExposure.Annotate(
            modelBuilder.Entity<SalesOrderDetail>(),
            McpFieldExposure.Safe,
            nameof(SalesOrderDetail.SalesOrderId), nameof(SalesOrderDetail.SalesOrderDetailId),
            nameof(SalesOrderDetail.OrderQty), nameof(SalesOrderDetail.ProductId), nameof(SalesOrderDetail.UnitPrice),
            nameof(SalesOrderDetail.UnitPriceDiscount), nameof(SalesOrderDetail.LineTotal), nameof(SalesOrderDetail.ModifiedDate));

        McpEntityExposure.Annotate(
            modelBuilder.Entity<SalesOrderHeader>(),
            McpFieldExposure.Safe,
            nameof(SalesOrderHeader.SalesOrderId), nameof(SalesOrderHeader.RevisionNumber),
            nameof(SalesOrderHeader.OrderDate), nameof(SalesOrderHeader.DueDate), nameof(SalesOrderHeader.ShipDate),
            nameof(SalesOrderHeader.Status), nameof(SalesOrderHeader.OnlineOrderFlag), nameof(SalesOrderHeader.SalesOrderNumber),
            nameof(SalesOrderHeader.PurchaseOrderNumber), nameof(SalesOrderHeader.AccountNumber), nameof(SalesOrderHeader.CustomerId),
            nameof(SalesOrderHeader.ShipToAddressId), nameof(SalesOrderHeader.BillToAddressId), nameof(SalesOrderHeader.ShipMethod),
            nameof(SalesOrderHeader.SubTotal), nameof(SalesOrderHeader.TaxAmt), nameof(SalesOrderHeader.Freight),
            nameof(SalesOrderHeader.TotalDue), nameof(SalesOrderHeader.Comment), nameof(SalesOrderHeader.ModifiedDate));
        McpEntityExposure.Annotate(
            modelBuilder.Entity<SalesOrderHeader>(),
            McpFieldExposure.Redact,
            nameof(SalesOrderHeader.CreditCardApprovalCode));
    }
}

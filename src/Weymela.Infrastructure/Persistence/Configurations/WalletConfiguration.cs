using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<BusinessWallet>
{
    public void Configure(EntityTypeBuilder<BusinessWallet> b)
    {
        b.ToTable("BusinessWallets", t => t.HasCheckConstraint("CK_Wallet_NonNegative", "\"AvailableBalance\" >= 0 AND \"ReservedBalance\" >= 0"));
        Mapping.Scalars(b, "TotalBalance", "DomainEvents");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => x.BusinessId);
        b.Property<decimal>("StoredTotalBalance").HasColumnName("TotalBalance").HasPrecision(18, 2)
            .HasComputedColumnSql("\"AvailableBalance\" + \"ReservedBalance\"", stored: true);
        Mapping.Version(b);
    }
}

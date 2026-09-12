using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Weymela.Domain;

namespace Weymela.Infrastructure.Persistence.Configurations;

internal static class Mapping
{
    public static readonly ValueConverter<Money, decimal> MoneyConverter = new(
        v => v.Amount, v => new Money(v, "ETB"));
    public static readonly ValueConverter<Money?, decimal?> NullableMoneyConverter = new(
        v => v.HasValue ? v.Value.Amount : null, v => v.HasValue ? new Money(v.Value, "ETB") : null);

    // Explicit mapping includes immutable getter-only domain properties (EF uses their backing fields).
    public static void Scalars<T>(EntityTypeBuilder<T> b, params string[] ignored) where T : class
    {
        foreach (var p in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (ignored.Contains(p.Name)) { b.Ignore(p.Name); continue; }
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t == typeof(Money)) Money(b.Property(p.Name), Nullable.GetUnderlyingType(p.PropertyType) != null);
            else if (t.IsEnum) b.Property(p.Name).HasConversion<string>().HasMaxLength(64);
            else if (t == typeof(decimal)) b.Property(p.Name).HasPrecision(9, 4);
            else if (t == typeof(string) || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(int) || t == typeof(long) || t == typeof(bool))
                b.Property(p.Name);
        }
    }

    public static void Money(PropertyBuilder b, bool nullable = false) =>
        b.HasConversion(nullable ? NullableMoneyConverter : MoneyConverter).HasPrecision(18, 2);

    public static void Pricing<TOwner>(OwnedNavigationBuilder<TOwner, PricingSnapshot> b) where TOwner : class
    {
        b.Ignore(x => x.IsValid);
        b.Property(x => x.PromotionType).HasConversion<string>().HasMaxLength(64);
        b.Property(x => x.ViewsPerReward);
        Money(b.Property(x => x.BusinessCharge));
        Money(b.Property(x => x.CreatorEarning));
        Money(b.Property(x => x.PlatformEarning));
        Money(b.Property(x => x.MinimumPromotionBudget), true);
        b.Property(x => x.CreatorCommissionPercent).HasPrecision(9, 4);
        b.Property(x => x.CustomerCashbackPercent).HasPrecision(9, 4);
        b.Property(x => x.PlatformPercent).HasPrecision(9, 4);
        b.Property(x => x.EffectiveFromUtc);
        b.Property(x => x.ConfigurationVersionId);
    }

    public static void Version<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.Property<long>("Version").IsConcurrencyToken();
        // xmin also protects against changes issued outside the normal repository path.
        b.Property<uint>("xmin").IsRowVersion();
    }
}

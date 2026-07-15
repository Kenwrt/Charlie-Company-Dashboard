using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<NotificationRecipient>(entity =>
        {
            entity.Property(recipient => recipient.DisplayName)
                .HasMaxLength(120);

            entity.Property(recipient => recipient.EmailAddress)
                .HasMaxLength(256);

            entity.Property(recipient => recipient.CellPhoneNumber)
                .HasMaxLength(32);
        });
    }
}

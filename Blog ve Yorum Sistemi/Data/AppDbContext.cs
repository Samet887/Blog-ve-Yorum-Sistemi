using BireyselHesaplar.Models;
using Microsoft.EntityFrameworkCore;

namespace BireyselHesaplar.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<BlogPost> BlogPosts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<BlogLike> BlogLikes { get; set; }
        public DbSet<CommentLike> CommentLikes { get; set; }
        public DbSet<UserBlock> UserBlocks { get; set; }
        public DbSet<UserBan> UserBans { get; set; }
        public DbSet<AdminActionLog> AdminActionLogs { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("dbo");

            modelBuilder.Entity<User>().ToTable("Users", "dbo");
            modelBuilder.Entity<BlogPost>().ToTable("BlogPosts", "dbo");
            modelBuilder.Entity<Comment>().ToTable("Comments", "dbo");
            modelBuilder.Entity<BlogLike>().ToTable("BlogLikes", "dbo");
            modelBuilder.Entity<CommentLike>().ToTable("CommentLikes", "dbo");
            modelBuilder.Entity<UserBlock>().ToTable("UserBlocks", "dbo");
            modelBuilder.Entity<UserBan>().ToTable("UserBans", "dbo");
            modelBuilder.Entity<AdminActionLog>().ToTable("AdminActionLogs", "dbo");
            modelBuilder.Entity<Category>().ToTable("Categories", "dbo");
            modelBuilder.Entity<PasswordResetToken>().ToTable("PasswordResetTokens", "dbo");

            modelBuilder.Entity<User>()
                .HasIndex(x => x.UserName)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(x => x.Email)
                .IsUnique();

            modelBuilder.Entity<BlogPost>()
                .HasOne(x => x.User)
                .WithMany(x => x.BlogPosts)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BlogPost>()
                .HasIndex(x => x.CategorySlug);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.CategorySlug)
                .HasMaxLength(80);

            modelBuilder.Entity<Comment>()
                .HasOne(x => x.BlogPost)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.BlogPostId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Comment>()
                .HasOne(x => x.User)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Comment>()
                .HasOne(x => x.ParentComment)
                .WithMany(x => x.Replies)
                .HasForeignKey(x => x.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BlogLike>()
                .HasOne(x => x.BlogPost)
                .WithMany(x => x.Likes)
                .HasForeignKey(x => x.BlogPostId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BlogLike>()
                .HasOne(x => x.User)
                .WithMany(x => x.Likes)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BlogLike>()
                .HasIndex(x => new { x.BlogPostId, x.UserId })
                .IsUnique();

            modelBuilder.Entity<CommentLike>()
                .HasOne(x => x.Comment)
                .WithMany(x => x.Likes)
                .HasForeignKey(x => x.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CommentLike>()
                .HasOne(x => x.User)
                .WithMany(x => x.CommentLikes)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CommentLike>()
                .HasIndex(x => new { x.CommentId, x.UserId })
                .IsUnique();

            modelBuilder.Entity<UserBlock>()
                .HasOne(x => x.BlockerUser)
                .WithMany(x => x.BlockedUsers)
                .HasForeignKey(x => x.BlockerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserBlock>()
                .HasOne(x => x.BlockedUser)
                .WithMany(x => x.BlockedByUsers)
                .HasForeignKey(x => x.BlockedUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserBlock>()
                .HasIndex(x => new { x.BlockerUserId, x.BlockedUserId })
                .IsUnique();

            modelBuilder.Entity<UserBan>()
                .HasOne(x => x.User)
                .WithMany(x => x.Bans)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserBan>()
                .HasOne(x => x.AdminUser)
                .WithMany(x => x.BansIssued)
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserBan>()
                .HasIndex(x => new { x.UserId, x.ExpiresAt });

            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PasswordResetToken>()
                .Property(x => x.TokenHash)
                .HasMaxLength(128);

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(x => new { x.UserId, x.ExpiresAt });

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(x => x.TokenHash)
                .IsUnique();

            modelBuilder.Entity<Category>()
                .HasIndex(x => x.Slug)
                .IsUnique();

            modelBuilder.Entity<Category>()
                .Property(x => x.Slug)
                .HasMaxLength(80);

            modelBuilder.Entity<Category>()
                .Property(x => x.Name)
                .HasMaxLength(80);

            modelBuilder.Entity<Category>()
                .HasOne(x => x.ParentCategory)
                .WithMany(x => x.SubCategories)
                .HasForeignKey(x => x.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Category>()
                .HasIndex(x => x.ParentCategoryId);
        }
    }
}

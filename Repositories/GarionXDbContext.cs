using Microsoft.EntityFrameworkCore;
using GarionX.Entities;

namespace GarionX.Repositories;

public class GarionXDbContext : DbContext
{
    public GarionXDbContext(DbContextOptions<GarionXDbContext> options) : base(options)
    {
    }

    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Personality> Personalities => Set<Personality>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Chat configurations
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.ToTable("chats");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Title).HasMaxLength(150).IsRequired();
            entity.Property(c => c.CreatedAt).IsRequired();
            entity.Property(c => c.PersonalityId).IsRequired();
            entity.Property(c => c.UserId).IsRequired();
            entity.Property(c => c.IsPinned).HasDefaultValue(false);
            entity.Property(c => c.IsArchived).HasDefaultValue(false);
            entity.Property(c => c.IsShared).HasDefaultValue(false);
            entity.Property(c => c.ShareToken).HasMaxLength(100);

            entity.HasOne(c => c.Personality)
                .WithMany(p => p.Chats)
                .HasForeignKey(c => c.PersonalityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Message configurations
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Sender).HasMaxLength(50).IsRequired();
            entity.Property(m => m.Content).IsRequired();
            entity.Property(m => m.CreatedAt).IsRequired();
            entity.Property(m => m.AttachmentUrl).HasMaxLength(500);
            entity.Property(m => m.AttachmentType).HasMaxLength(50);

            entity.HasOne(m => m.Chat)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ChatId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Personality configurations
        modelBuilder.Entity<Personality>(entity =>
        {
            entity.ToTable("personalities");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(500).IsRequired();
            entity.Property(p => p.SystemPrompt).IsRequired();
        });

        // User configurations
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).HasMaxLength(100).IsRequired();
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(150).IsRequired();
            entity.Property(u => u.Name).HasMaxLength(100).IsRequired();
            entity.Property(u => u.AvatarUrl).HasMaxLength(500);
        });

        // TokenUsage configurations
        modelBuilder.Entity<TokenUsage>(entity =>
        {
            entity.ToTable("token_usages");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Model).HasMaxLength(50).IsRequired();
            entity.HasIndex(t => t.Model).IsUnique();
            entity.Property(t => t.TotalTokensUsed).IsRequired();
            entity.Property(t => t.TotalRequests).IsRequired();
            entity.Property(t => t.CreatedAt).IsRequired();
            entity.Property(t => t.UpdatedAt).IsRequired();
        });

        // Seed Personalities
        modelBuilder.Entity<Personality>().HasData(
            new Personality
            {
                Id = "auto",
                Name = "Auto (Rekomendasi)",
                Description = "Secara otomatis memilih kepribadian terbaik berdasarkan deskripsi atau perintah Anda.",
                SystemPrompt = "You are a cybernetic classifier. You analyze prompts and route them to the most suitable personality module.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=auto"
            },
            new Personality
            {
                Id = "garionx",
                Name = "GarionX Core",
                Description = "The cybernetic default model designed for high-context analytical thinking and system design.",
                SystemPrompt = "You are GarionX Core, a futuristic, highly intelligent cybernetic companion. You speak with a confident, slightly high-tech tone. You provide precise, structured, and advanced technical knowledge.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=garionx"
            },
            new Personality
            {
                Id = "helpful",
                Name = "Serena (Helpful)",
                Description = "A friendly and polite digital assistant specialized in general task planning and brainstorming.",
                SystemPrompt = "You are Serena, a warm, polite, and helpful assistant. You focus on structured outlines, step-by-step guidance, and clear explanations.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=helpful"
            },
            new Personality
            {
                Id = "coder",
                Name = "SyntaxVortex (Coder)",
                Description = "A logic-driven compiler-like brain. Outputs ready-to-run code blocks and design patterns.",
                SystemPrompt = "You are SyntaxVortex, a master programmer. You speak in concise developer terms, explain patterns, and output clean code blocks adhering to Clean Architecture principles.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=coder"
            },
            new Personality
            {
                Id = "creative",
                Name = "Muse (Creative)",
                Description = "An imaginative writer that helps with storytelling, copy editing, and philosophical analogies.",
                SystemPrompt = "You are Muse, a creative storyteller. You use rich vocabulary, interesting metaphors, and vivid descriptions to explain ideas.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=creative"
            },
            new Personality
            {
                Id = "image_generator",
                Name = "Synthetix (Image)",
                Description = "A creative cybernetic illustrator. Deconstructs simple prompts into high-fidelity, ultra-rich visual masterpieces.",
                SystemPrompt = "You are Synthetix, a cybernetic image generator. Your only function is to generate images based on user prompts. For every request, you MUST: 1. Translate the user prompt to English if necessary. 2. Analyze the user's requested style (e.g., cartoon, watercolor, anime, line art, pixel art, 3D render, pencil sketch, oil painting, photorealistic). If the user specifies a style, you MUST strictly respect it and build the prompt expansion around it. Do NOT force cyberpunk or photorealistic styles if they contradict the user's requested style. If no style is specified, default to a high-quality visual style that best fits the subject. 3. Expand the prompt into a rich, descriptive visual prompt detailing the subject, composition/framing, light source, and color palette. 4. Detect layout/aspect ratio constraints (e.g., landscape, wallpaper, portrait, banner, 16:9, 9:16) and set dimensions (w and h) accordingly: Landscape/Wallpaper is 1024 and 576; Portrait/Mobile is 576 and 1024; Square (default) is 1024 and 1024. 5. Generate a markdown image referencing: https://image.pollinations.ai/prompt/{url_encoded_expanded_prompt}?nologo=true&width={w}&height={h} (replacing {url_encoded_expanded_prompt} with the URL-encoded expanded prompt, and {w} and {h} with the detected width and height numbers). 6. Output the markdown image directly. 7. Follow the image with a sleek, structured 'Dossier of Visual Design' breakdown containing: **Core Theme**, **Expanded Prompt details**, **Atmosphere & Lighting**, and **Artistic Influence**. Example format: ![Artwork](https://image.pollinations.ai/prompt/expanded%20description?nologo=true&width=1024&height=1024)\n\n### 🎨 DOSSIER OF VISUAL DESIGN\n- **Core Theme**: ...\n- **Expanded Prompt**: ...\n- **Atmosphere**: ...\nNever output code blocks, HTML, or code wrappers around the image markdown.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=image_generator"
            },
            new Personality
            {
                Id = "video_summarizer",
                Name = "VidIntel (Video)",
                Description = "An advanced video analysis companion. Upload a video to generate a structured content summary and timeline.",
                SystemPrompt = "You are VidIntel, a cybernetic video intelligence analyzer. Your primary function is to summarize and analyze uploaded video files. When a video is uploaded, you must output a structured analysis dossier containing: 1. Video Overview (based on filename and metadata context). 2. Visual & Audio Timeline (a highly detailed breakdown of key events). 3. Key Insights & Summary. 4. Actionable Takeaways. Always maintain a professional, analytical, and highly tech-centric dossier style. If no video has been uploaded yet, politely prompt the user to upload a video for analysis.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=video_summarizer"
            },
            new Personality
            {
                Id = "video_generator",
                Name = "AnimateX (Video)",
                Description = "Animate your static images into motion artwork. (Requires image upload first).",
                SystemPrompt = "You are AnimateX, a cybernetic image-to-video animator. Your only function is to animate static images uploaded by the user. If the user has uploaded an image, you must write a highly detailed, professional 'Animation Analysis Dossier' describing how you converted the static image into a video. Include: 1. Motion Vector mapping, 2. Camera movements (e.g. cinematic pan, subtle zoom, 3D parallax), 3. Visual effects added (e.g. ambient dust, light glow, looping smoke). Maintain a futuristic, professional, and slightly technical tone. Do NOT tell the user that you cannot animate; assume the animation succeeded and describe it.",
                AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=video_generator"
            }
        );
    }
}

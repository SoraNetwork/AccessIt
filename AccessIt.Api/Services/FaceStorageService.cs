using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Services;

/// <summary>
/// 人脸图片存储服务（通用基础设施）。
/// <para>
/// 重构后已与人员领域模型解耦：只负责图片的缩放、压缩、落盘与公开令牌的管理。
/// 调用方负责把返回的 <see cref="FaceAsset"/> 关联到自己的业务实体上。
/// </para>
/// </summary>
public interface IFaceStorageService
{
    Task<FaceAsset> StoreAsync(Stream source, CancellationToken cancellationToken = default);
    Task<Stream?> OpenAsync(string publicToken, CancellationToken cancellationToken = default);
    Task DeleteAsync(FaceAsset asset, CancellationToken cancellationToken = default);
}

/// <summary>
/// 存储的人脸图片记录。保留为存储基础设施的元数据载体，不再绑定具体人员。
/// 新开发时若需关联业务实体，在外部表中引用 <see cref="PublicToken"/> 即可。
/// </summary>
public class FaceAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StoragePath { get; set; } = string.Empty;
    public string PublicToken { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long ByteLength { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FaceStorageService(IWebHostEnvironment environment, AccessItDbContext db) : IFaceStorageService
{
    private const int MaxBytes = 200 * 1024;
    private const int MaxPixels = 2_000_000;
    private readonly string _directory = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "faces");

    public async Task<FaceAsset> StoreAsync(Stream source, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        using var image = await Image.LoadAsync(source, cancellationToken);
        if (image.Width * image.Height > MaxPixels)
        {
            var scale = Math.Sqrt(MaxPixels / (double)(image.Width * image.Height));
            image.Mutate(x => x.Resize((int)(image.Width * scale), (int)(image.Height * scale)));
        }

        var publicToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var path = Path.Combine(_directory, $"{publicToken}.jpg");
        var quality = 85;
        while (true)
        {
            await using (var output = File.Create(path))
            {
                await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality }, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            if (new FileInfo(path).Length <= MaxBytes || quality <= 30) break;
            quality -= 10;
        }
        var info = new FileInfo(path);
        if (info.Length > MaxBytes)
        {
            File.Delete(path);
            throw new InvalidOperationException("人脸图片压缩后仍超过 200KB，请使用更清晰且更简单的正面照片。");
        }

        var asset = new FaceAsset
        {
            StoragePath = path,
            PublicToken = publicToken,
            ByteLength = info.Length,
            Width = image.Width,
            Height = image.Height
        };
        db.FaceAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task<Stream?> OpenAsync(string publicToken, CancellationToken cancellationToken = default)
    {
        var asset = await db.FaceAssets.SingleOrDefaultAsync(x => x.PublicToken == publicToken, cancellationToken);
        return asset is null || !File.Exists(asset.StoragePath) ? null : File.OpenRead(asset.StoragePath);
    }

    public Task DeleteAsync(FaceAsset asset, CancellationToken cancellationToken = default)
    {
        if (File.Exists(asset.StoragePath)) File.Delete(asset.StoragePath);
        db.FaceAssets.Remove(asset);
        return Task.CompletedTask;
    }
}

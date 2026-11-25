using AI.Digest.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.Digest.Api.Data;

public sealed class DigestDbContext(DbContextOptions<DigestDbContext> options)
    : DbContext(options)
{
}

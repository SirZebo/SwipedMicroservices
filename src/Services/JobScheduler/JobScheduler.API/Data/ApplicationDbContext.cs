﻿using Microsoft.EntityFrameworkCore;

namespace JobScheduler.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions options) : base(options)
    {
    }
}

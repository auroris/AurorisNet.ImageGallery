using System;
using System.IO;
using System.Reflection;
using ImageMagick;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xabe.FFmpeg;

namespace ImageGallery
{
	/// <summary>
	/// Application entry point. Exposes <see cref="AppConfiguration"/> as a static property so the
	/// <see cref="Configuration"/> singleton can read settings before the DI container is available.
	/// </summary>
	public class Program
	{
		/// <summary>
		/// The application's <see cref="IConfiguration"/> instance, set during startup and used by
		/// <see cref="Configuration.Get"/> to read values from appsettings.json.
		/// </summary>
		public static IConfiguration AppConfiguration { get; private set; }

		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			// Route all logging through log4net using the XML config file in the app directory
			builder.Logging.ClearProviders();
			builder.Logging.AddLog4Net(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.xml"));
			builder.Logging.SetMinimumLevel(LogLevel.Debug);

			builder.Services.AddResponseCaching();
			builder.Services.AddControllers();
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen();

			var app = builder.Build();

			// Make configuration available to the Configuration singleton before anything else reads it
			AppConfiguration = builder.Configuration;
			log4net.Config.XmlConfigurator.Configure(new FileInfo(Configuration.Get.LogDir));

			if (app.Environment.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
				app.UseSwagger();
				app.UseSwaggerUI();
			}
			else
			{
				app.UseExceptionHandler(errorApp =>
				{
					errorApp.Run(async context =>
					{
						var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
							.CreateLogger("UnhandledException");
						var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
						if (exceptionFeature?.Error != null)
						{
							logger.LogError(exceptionFeature.Error, "Unhandled exception on {Method} {Path}",
								context.Request.Method, context.Request.Path);
						}
						context.Response.StatusCode = 500;
						context.Response.ContentType = "application/json";
						await context.Response.WriteAsync(
							System.Text.Json.JsonSerializer.Serialize(new { error = "An internal server error occurred." }));
					});
				});
				app.UseStatusCodePages("text/plain", "{0}");
			}

			app.UseStaticFiles();
			app.UseRouting();
			app.UseResponseCaching();
			app.MapControllers();

			// Prepare the temp directory used by ImageMagick and FFmpeg during thumbnail generation
			Directory.CreateDirectory(Configuration.Get.TempDir);
			string tempProbeFile = Path.Combine(Configuration.Get.TempDir, ".write_test_" + Guid.NewGuid());
			try
			{
				File.WriteAllText(tempProbeFile, "");
				File.Delete(tempProbeFile);
			}
			catch (Exception ex)
			{
				var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
				startupLogger.LogCritical(ex, "Temp directory is not writable: {TempDir}", Configuration.Get.TempDir);
				throw new InvalidOperationException(
					$"Temp directory is not writable: {Configuration.Get.TempDir} — check that the path exists and the application has write permissions.", ex);
			}
			MagickNET.SetTempDirectory(Configuration.Get.TempDir);
			FFmpeg.SetExecutablesPath(Path.Combine(builder.Environment.ContentRootPath, "ffmpeg"));

			app.Run();
		}
	}
}

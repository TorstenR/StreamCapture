using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace StreamCapture
{
    class Startup
    {
        //Holds context
        public Recordings recordings;

        public void Configure(IApplicationBuilder app)
        {
            app.Run(async context => 
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("Hello world<p>");

                //grab my object
                foreach(RecordInfo recordInfo in recordings.GetRecordInfoList())
                {
                    await context.Response.WriteAsync($"{recordInfo.description}<br>");
                }
                return;
            });
        }
    }
}


/*
 * setting up kestrel: https://docs.microsoft.com/en-us/aspnet/core/getting-started
 * DI: https://msdn.microsoft.com/en-us/magazine/mt707534.aspx
 * 
 * 
 * /

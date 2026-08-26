using System;
using System.Threading;
using OpenMedia.SDK;

namespace StreamingApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("OpenMedia Streaming CLI");
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: StreamingApp <input_file> <output_url>");
                return;
            }

            string input = args[0];
            string output = args[1];

            Console.WriteLine($"Starting stream from {input} to {output}");

            // Init engine
            NativeBridge.ome_engine_init("{\"mode\": \"cli\"}");

            // Create pipeline
            var pipeline = NativeBridge.ome_pipeline_create();
            
            // Create source
            var inputStr = NativeStringHelper.StringToUtf8Pointer(input);
            var source = NativeBridge.ome_source_create_file(input);
            NativeStringHelper.FreeUtf8Pointer(inputStr);

            // Create output
            var outputStr = NativeStringHelper.StringToUtf8Pointer(output);
            var rtmpOutput = NativeBridge.om_create_srt_output(output); // Assume generic output or specific
            NativeStringHelper.FreeUtf8Pointer(outputStr);

            // Add to pipeline
            NativeBridge.ome_pipeline_add_node(pipeline, source);
            NativeBridge.ome_pipeline_add_node(pipeline, rtmpOutput);

            // Start
            NativeBridge.ome_pipeline_start(pipeline);

            Console.WriteLine("Streaming... Press Enter to stop.");
            Console.ReadLine();

            // Stop and cleanup
            NativeBridge.ome_pipeline_stop(pipeline);
            
            NativeBridge.ome_source_destroy(source);
            NativeBridge.ome_output_destroy(rtmpOutput);
            NativeBridge.ome_pipeline_destroy(pipeline);
            NativeBridge.ome_engine_shutdown();

            Console.WriteLine("Done.");
        }
    }
}

// See https://aka.ms/new-console-template for more information
using Newtonsoft.Json;
using Python.Runtime;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;


public class Program
{
    public static void Main()
    {
        Runtime.PythonDLL = "C:\\Program Files\\Python\\Python39\\python39.dll";

        PythonEngine.Initialize();
        PythonEngine.BeginAllowThreads();
        
        // Rerank with two values
        var a = "你好";
        var b = "Hi";

        var score = ComputeScore(a, b);
        Console.WriteLine($"{a} <-> {b}: {score}");

        // Rerank with multiple pairs values
        var documents = new List<string>() { "Hi", "Hello", "How are you?", "How do you do?" };
        var documents_with_scores = ComputeScore(a, documents);
        foreach (var item in documents_with_scores)
        {
            Console.WriteLine($"{a} <-> {item.Item1}: {item.Item2}");
        }

        // Embedding
        var vectors = Embedding("Xorbits/bge-base-zh-v1.5", "问君能有几多愁，恰似一江春水向东流");
        Console.WriteLine(JsonConvert.SerializeObject(vectors));

        // Completion
        Console.WriteLine(Completion("你好"));
    }

    public static double ComputeScore(string a, string b)
    {
        using (Py.GIL())
        {

            dynamic modelscope = Py.Import("modelscope");
            dynamic flagEmbedding = Py.Import("FlagEmbedding");

            dynamic rerankModelDir = modelscope.snapshot_download("Xorbits/bge-base-zh-v1.5", revision: "master");
            dynamic flagReranker = flagEmbedding.FlagReranker(rerankModelDir, use_fp16: true);

            var pyList = new PyList();
            pyList.Append(a.ToPython());
            pyList.Append(b.ToPython());

            PyObject result = flagReranker.compute_score(pyList, normalize: true);
            return result.As<double>();
        }
    }

    public static List<Tuple<string, double>> ComputeScore(string question, List<string> documents)
    {
        using (Py.GIL())
        {

            dynamic modelscope = Py.Import("modelscope");
            dynamic flagEmbedding = Py.Import("FlagEmbedding");

            dynamic rerankModelDir = modelscope.snapshot_download("Xorbits/bge-base-zh-v1.5", revision: "master");
            dynamic flagReranker = flagEmbedding.FlagReranker(rerankModelDir, use_fp16: true);


            var pyList = new PyList();
            foreach (var document in documents)
            {
                var pair = new PyList();
                pair.Append(question.ToPython());
                pair.Append(document.ToPython());
                pyList.Append(pair.ToPython());
            }

            PyObject result = flagReranker.compute_score(pyList, normalize: true);
            var scores = result.As<PyList>().AsList<double>().ToList();

            var results = new List<Tuple<string, double>>();
            for (var i = 0; i < scores.Count; i++)
            {
                results.Add(new Tuple<string, double>(documents[i], scores[i]));
            }

            return results;
        }
    }

    public static List<double> Embedding(string modelName,string text)
    {
        using (Py.GIL())
        {
            dynamic modelscope = Py.Import("modelscope");
            dynamic sentenceTransformers = Py.Import("sentence_transformers");

            dynamic embeddingModelDir = modelscope.snapshot_download(modelName, revision: "master");
            dynamic embeddingModel = sentenceTransformers.SentenceTransformer(embeddingModelDir);
            PyObject vectors = embeddingModel.encode(text.ToPython()).tolist();
            return vectors.As<PyList>().AsList<double>().ToList();
        }
    }

    public static string Completion(string query)
    {
        using (Py.GIL())
        {
            dynamic transformers = Py.Import("transformers");
            dynamic modelscope = Py.Import("modelscope");

            var modelName = "LLM-Research/Phi-3-mini-4k-instruct";
            dynamic modelDir = modelscope.snapshot_download(modelName, revision: "master");

            dynamic pipe = transformers.pipeline("text-generation", model: modelDir, trust_remote_code: true.ToPython());

            dynamic output = pipe(query);
            return output.As<string>();
        }
    }
}

public static class PyListExtensions
{
    public static IEnumerable<T> AsList<T>(this PyList pyList)
    {
        foreach (var item in pyList)
        {
            yield return item.As<T>();
        }
    }
}



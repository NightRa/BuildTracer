using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Converters;

namespace BuildTracer
{
    public sealed class NinjaSyntax
    {
        private StringBuilder _output = new StringBuilder();

        private static String EscapePath(String path)
        {
            return path
                .Replace("$ ", "$$ ")
                .Replace(" ", "$ ")
                .Replace(":", "$:");
        }

        public void Line(String text, int indent = 0)
        {
            _output.Append(new String(' ', indent))
                .Append(text)
                .Append('\n');
        }

        public void Newline()
        {
            _output.Append('\n');
        }

        public void Comment(String text)
        {
            _output.Append("# ").Append(text).Append('\n');
        }

        public void Variable(String key, String value, int indent = 0)
        {
            this.Line($"{key} = {value}", indent);
        }

        public void Variable(String key, IEnumerable<String> values, int indent = 0)
        {
            this.Variable(key, String.Join(" ", values), indent);
        }

        public void Pool(String name, int depth)
        {
            this.Line($"Pool {name}");
            this.Variable("depth", depth.ToString(), indent: 1);
        }

        public void Rule(String name, String command,
            String? description = null, String? depfile = null,
            bool generator = false, String? pool = null,
            bool restat = false, String? rspFile = null,
            String? rspFileContent = null, String? deps = null)
        {
            this.Line($"rule {name}");
            this.Variable($"command", command, indent: 1);
            if (description != null)
            {
                this.Variable("description", description, indent: 1);
            }

            if (depfile != null)
            {
                this.Variable("depfile", depfile, indent: 1);
            }

            if (generator)
            {
                this.Variable("generator", "1", indent: 1);
            }

            if (pool != null)
            {
                this.Variable("pool", pool, indent: 1);
            }

            if (restat)
            {
                this.Variable("restat", "1", indent: 1);
            }

            if (rspFile != null)
            {
                this.Variable("rspfile", rspFile, indent: 1);
            }

            if (rspFileContent != null)
            {
                this.Variable("rspfile_content", rspFileContent, indent: 1);
            }

            if (deps != null)
            {
                this.Variable("deps", deps, indent: 1);
            }
        }

        public static IEnumerable<String> None = Enumerable.Empty<String>();

        private IEnumerable<T> Single<T>(T elem)
        {
            yield return elem;
        }

        public void Build(
            IEnumerable<String> outputs, String rule,
            IEnumerable<String> inputs, IEnumerable<String> implicitInputs,
            IEnumerable<String> orderOnlyInputs, IEnumerable<(String key, String value)> variables,
            IEnumerable<String> implicitOutputs, String? pool)
        {
            var _outputs = outputs.Select(EscapePath).ToList();
            var _inputs = inputs.Select(EscapePath).ToList();
            var _implicitInputs = implicitInputs.Select(EscapePath).ToArray();
            var _orderOnlyInputs = orderOnlyInputs.Select(EscapePath).ToArray();
            var _implicitOutputs = implicitOutputs.Select(EscapePath).ToArray();

            if (_implicitInputs.Length > 0)
            {
                _inputs.Add("|");
                _inputs.AddRange(_implicitInputs);
            }

            if (_orderOnlyInputs.Any())
            {
                _inputs.Add("||");
                _inputs.AddRange(_orderOnlyInputs);
            }

            if (_implicitOutputs.Any())
            {
                _outputs.Add("|");
                _outputs.AddRange(_implicitOutputs);
            }

            this.Line($"build {String.Join(' ', _outputs)}: {rule} {String.Join(' ', _inputs)}");

            if (pool != null)
            {
                this.Variable("pool", pool, indent: 1);
            }

            foreach (var (key, value) in variables)
            {
                this.Variable(key, value, indent: 1);
            }
        }

        public void Include(String path)
        {
            this.Line($"include {path}");
        }

        public void Subninja(String path)
        {
            this.Line($"subninja {path}");
        }

        public void Default(IEnumerable<String> targets)
        {
            this.Line($"default {String.Join(' ', targets)}");
        }

        public override string ToString()
        {
            return _output.ToString();
        }
    }
}

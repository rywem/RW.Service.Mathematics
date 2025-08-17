using AngouriMath;
using AngouriMath.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RW.Service.Mathematics.Solvers
{
    /// <summary>
    ///  Source: https://am.angouri.org/demo/#demos
    /// </summary>
    public class AngouriMathService
    {
        
        public AngouriMathService()
        {
            // Initialize any required resources or configurations here
        }

        /// <summary>
        /// Demonstrates how to use AngouriMath to solve a simple algebraic equation.
        /// </summary>
        /// <returns>A list of solutions as strings.</returns>
        public AngouriMath.Entity.Set EquationSolverSample()
        {
            // Example: Solve x^2 - 4 = 0
            var equation = "x^2 - 4 = 0";
            Entity entity = equation.ToEntity();
            var variable = "x";
            Debug.WriteLine(entity.Differentiate("x"));
            // Solve the equation for x
            AngouriMath.Entity.Set solutions = entity.Solve(variable);

            // Convert solutions to string list
            return solutions; //?.Select(sol => sol.ToString()).ToList();
        }
    }
}

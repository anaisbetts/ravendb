// //-----------------------------------------------------------------------
// // <copyright company="Hibernating Rhinos LTD">
// //     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// // </copyright>
// //-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Raven.Abstractions.Data;
using Raven.Client.Document;
using Raven.Imports.Newtonsoft.Json;
using Raven.Client.Linq;
using Raven.Imports.Newtonsoft.Json.Utilities;
using Raven.Json.Linq;
#if !NETFX_CORE
using Raven.Abstractions.MissingFromBCL;
#endif

namespace Raven.Client.Indexes
{
	/// <summary>
	///   Based off of System.Linq.Expressions.ExpressionStringBuilder
	/// </summary>
	public class ExpressionStringBuilder : ExpressionVisitor
	{
		// Fields
		private static readonly char[] LiteralSymbolsToEscape = new[] { '\'', '\"', '\\', '\a', '\b', '\f', '\n', '\r', '\t', '\v' };
		private static readonly string[] LiteralEscapedSymbols = new[] { @"\'", @"\""", @"\\", @"\a", @"\b", @"\f", @"\n", @"\r", @"\t", @"\v" };

		private readonly StringBuilder _out = new StringBuilder();
		private readonly DocumentConvention convention;
		private readonly Type queryRoot;
		private readonly string queryRootName;
		private readonly bool translateIdentityProperty;
		private ExpressionOperatorPrecedence _currentPrecedence;
		private Dictionary<object, int> _ids;
		private bool castLambdas;

		// Methods
		private ExpressionStringBuilder(DocumentConvention convention, bool translateIdentityProperty, Type queryRoot,
										string queryRootName)
		{
			this.convention = convention;
			this.translateIdentityProperty = translateIdentityProperty;
			this.queryRoot = queryRoot;
			this.queryRootName = queryRootName;
		}

		private void AddLabel(LabelTarget label)
		{
			if (_ids == null)
			{
				_ids = new Dictionary<object, int> { { label, 0 } };
			}
			else if (!_ids.ContainsKey(label))
			{
				_ids.Add(label, _ids.Count);
			}
		}

		private void AddParam(ParameterExpression p)
		{
			if (_ids == null)
			{
				_ids = new Dictionary<object, int>();
				_ids.Add(_ids, 0);
			}
			else if (!_ids.ContainsKey(p))
			{
				_ids.Add(p, _ids.Count);
			}
		}

		internal string CatchBlockToString(CatchBlock node)
		{
			var builder = new ExpressionStringBuilder(convention, translateIdentityProperty, queryRoot, queryRootName);
			builder.VisitCatchBlock(node);
			return builder.ToString();
		}

		private void DumpLabel(LabelTarget target)
		{
			if (!string.IsNullOrEmpty(target.Name))
			{
				Out(target.Name);
			}
			else
			{
				Out("UnnamedLabel_" + GetLabelId(target));
			}
		}

		internal string ElementInitBindingToString(ElementInit node)
		{
			var builder = new ExpressionStringBuilder(convention, translateIdentityProperty, queryRoot, queryRootName);
			builder.VisitElementInit(node);
			return builder.ToString();
		}

		/// <summary>
		///   Convert the expression to a string
		/// </summary>
		public static string ExpressionToString(DocumentConvention convention, bool translateIdentityProperty, Type queryRoot,
												string queryRootName, Expression node)
		{
			var builder = new ExpressionStringBuilder(convention, translateIdentityProperty, queryRoot, queryRootName);
			builder.Visit(node, ExpressionOperatorPrecedence.ParenthesisNotNeeded);
			return builder.ToString();
		}

		private static string FormatBinder(CallSiteBinder binder)
		{
			var binder2 = binder as ConvertBinder;
			if (binder2 != null)
			{
				return ("Convert " + binder2.Type);
			}
			var binder3 = binder as GetMemberBinder;
			if (binder3 != null)
			{
				return ("GetMember " + binder3.Name);
			}
			var binder4 = binder as SetMemberBinder;
			if (binder4 != null)
			{
				return ("SetMember " + binder4.Name);
			}
			var binder5 = binder as DeleteMemberBinder;
			if (binder5 != null)
			{
				return ("DeleteMember " + binder5.Name);
			}
			if (binder is GetIndexBinder)
			{
				return "GetIndex";
			}
			if (binder is SetIndexBinder)
			{
				return "SetIndex";
			}
			if (binder is DeleteIndexBinder)
			{
				return "DeleteIndex";
			}
			var binder6 = binder as InvokeMemberBinder;
			if (binder6 != null)
			{
				return ("Call " + binder6.Name);
			}
			if (binder is InvokeBinder)
			{
				return "Invoke";
			}
			if (binder is CreateInstanceBinder)
			{
				return "Create";
			}
			var binder7 = binder as UnaryOperationBinder;
			if (binder7 != null)
			{
				return binder7.Operation.ToString();
			}
			var binder8 = binder as BinaryOperationBinder;
			if (binder8 != null)
			{
				return binder8.Operation.ToString();
			}
			return "CallSiteBinder";
		}

		private int GetLabelId(LabelTarget label)
		{
			int count;
			if (_ids == null)
			{
				_ids = new Dictionary<object, int>();
				AddLabel(label);
				return 0;
			}
			if (!_ids.TryGetValue(label, out count))
			{
				count = _ids.Count;
				AddLabel(label);
			}
			return count;
		}

		private int GetParamId(ParameterExpression p)
		{
			int count;
			if (_ids == null)
			{
				_ids = new Dictionary<object, int>();
				AddParam(p);
				return 0;
			}
			if (!_ids.TryGetValue(p, out count))
			{
				count = _ids.Count;
				AddParam(p);
			}
			return count;
		}

		internal string MemberBindingToString(MemberBinding node)
		{
			var builder = new ExpressionStringBuilder(convention, translateIdentityProperty, queryRoot, queryRootName);
			builder.VisitMemberBinding(node);
			return builder.ToString();
		}

		private void Out(char c)
		{
			_out.Append(c);
		}

		private void Out(string s)
		{
			_out.Append(s);
		}

		private void OutMember(Expression instance, MemberInfo member, Type exprType)
		{
			var name = GetPropertyName(member.Name, exprType);
            if (TranslateToDocumentId(instance, member, exprType))
			{
				name = Constants.DocumentIdFieldName;
			}
			if (instance != null)
			{
				if (ShouldParantesisMemberExpression(instance))
					Out("(");
				Visit(instance);
				if (ShouldParantesisMemberExpression(instance))
					Out(")");
				Out("." + name);
			}
			else
			{
				var parentType = member.DeclaringType;
				while (parentType.IsNested)
				{
					parentType = parentType.DeclaringType;
					if (parentType == null)
						break;
					Out(parentType.Name + ".");
				}

				Out(member.DeclaringType.Name + "." + name);
			}
		}

		private static bool ShouldParantesisMemberExpression(Expression instance)
		{
			switch (instance.NodeType)
			{
				case ExpressionType.Parameter:
				case ExpressionType.MemberAccess:
					return false;
				default:
					return true;
			}
		}

        private bool TranslateToDocumentId(Expression instance, MemberInfo member, Type exprType)
        {
            if (translateIdentityProperty == false)
                return false;

            if (convention.GetIdentityProperty(member.DeclaringType) != member)
                return false;

            // only translate from the root type or derivatives
            if (queryRoot != null && (exprType.IsAssignableFrom(queryRoot) == false))
                return false;

            if (queryRootName == null)
                return true; // just in case, shouldn't really happen

            // only translate from the root alias
            string memberName = null;
            while (true)
            {
                switch (instance.NodeType)
                {
                    case ExpressionType.MemberAccess:
                        var memberExpression = ((MemberExpression) instance);
                        if (memberName == null)
                        {
                            memberName = memberExpression.Member.Name;
                        }
                        instance = memberExpression.Expression;
                        break;
                    case ExpressionType.Parameter:
                        var parameterExpression = ((ParameterExpression) instance);
                        if (memberName == null)
                        {
                            memberName = parameterExpression.Name;
                        }
                        return memberName == queryRootName;
                    default:
                        return false;
                }
            }

        }

		private string GetPropertyName(string name, Type exprType)
		{
			var propertyInfo = exprType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) ??
							   exprType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(x => x.Name == name);
			if (propertyInfo != null)
			{
				foreach (var customAttribute in propertyInfo.GetCustomAttributes(true))
				{
					string propName;
					switch (customAttribute.GetType().Name)
					{
						case "JsonPropertyAttribute":
							propName = ((dynamic)customAttribute).PropertyName;
							break;
						case "DataMemberAttribute":
							propName = ((dynamic)customAttribute).Name;
							break;
						default:
							continue;
					}
					if (keywordsInCSharp.Contains(propName))
						return '@' + propName;
					return propName ?? name;
				}
			}
			return name;
		}


		private static Type GetMemberType(MemberInfo member)
		{
			var prop = member as PropertyInfo;
			if (prop != null)
				return prop.PropertyType;
			return ((FieldInfo)member).FieldType;
		}

		internal string SwitchCaseToString(SwitchCase node)
		{
			var builder = new ExpressionStringBuilder(convention, translateIdentityProperty, queryRoot, queryRootName);
			builder.VisitSwitchCase(node);
			return builder.ToString();
		}

		/// <summary>
		///   Returns a <see cref = "System.String" /> that represents this instance.
		/// </summary>
		/// <returns>
		///   A <see cref = "System.String" /> that represents this instance.
		/// </returns>
		public override string ToString()
		{
			return _out.ToString();
		}

		private void SometimesParenthesis(ExpressionOperatorPrecedence outer, ExpressionOperatorPrecedence inner,
										  Action visitor)
		{
			var needParenthesis = outer.NeedsParenthesisFor(inner);

			if (needParenthesis)
				Out("(");

			visitor();

			if (needParenthesis)
				Out(")");
		}

		private void Visit(Expression node, ExpressionOperatorPrecedence outerPrecedence)
		{
			var previous = _currentPrecedence;
			_currentPrecedence = outerPrecedence;
			Visit(node);
			_currentPrecedence = previous;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.BinaryExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitBinary(BinaryExpression node)
		{
			return VisitBinary(node, _currentPrecedence);
		}

		private Expression VisitBinary(BinaryExpression node, ExpressionOperatorPrecedence outerPrecedence)
		{
			ExpressionOperatorPrecedence innerPrecedence;

			string str;
			var leftOp = node.Left;
			var rightOp = node.Right;

			FixupEnumBinaryExpression(ref leftOp, ref rightOp);
			switch (node.NodeType)
			{
				case ExpressionType.Add:
					str = "+";
					innerPrecedence = ExpressionOperatorPrecedence.Additive;
					break;

				case ExpressionType.AddChecked:
					str = "+";
					innerPrecedence = ExpressionOperatorPrecedence.Additive;
					break;

				case ExpressionType.And:
					if ((node.Type != typeof(bool)) && (node.Type != typeof(bool?)))
					{
						str = "&";
						innerPrecedence = ExpressionOperatorPrecedence.LogicalAND;
					}
					else
					{
						str = "And";
						innerPrecedence = ExpressionOperatorPrecedence.ConditionalAND;
					}
					break;

				case ExpressionType.AndAlso:
					str = "&&";
					innerPrecedence = ExpressionOperatorPrecedence.ConditionalAND;
					break;

				case ExpressionType.Coalesce:
					str = "??";
					innerPrecedence = ExpressionOperatorPrecedence.NullCoalescing;
					break;

				case ExpressionType.Divide:
					str = "/";
					innerPrecedence = ExpressionOperatorPrecedence.Multiplicative;
					break;

				case ExpressionType.Equal:
					str = "==";
					innerPrecedence = ExpressionOperatorPrecedence.Equality;
					break;

				case ExpressionType.ExclusiveOr:
					str = "^";
					innerPrecedence = ExpressionOperatorPrecedence.LogicalXOR;
					break;

				case ExpressionType.GreaterThan:
					str = ">";
					innerPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
					break;

				case ExpressionType.GreaterThanOrEqual:
					str = ">=";
					innerPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
					break;

				case ExpressionType.LeftShift:
					str = "<<";
					innerPrecedence = ExpressionOperatorPrecedence.Shift;
					break;

				case ExpressionType.LessThan:
					str = "<";
					innerPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
					break;

				case ExpressionType.LessThanOrEqual:
					str = "<=";
					innerPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
					break;

				case ExpressionType.Modulo:
					str = "%";
					innerPrecedence = ExpressionOperatorPrecedence.Multiplicative;
					break;

				case ExpressionType.Multiply:
					str = "*";
					innerPrecedence = ExpressionOperatorPrecedence.Multiplicative;
					break;

				case ExpressionType.MultiplyChecked:
					str = "*";
					innerPrecedence = ExpressionOperatorPrecedence.Multiplicative;
					break;

				case ExpressionType.NotEqual:
					str = "!=";
					innerPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
					break;

				case ExpressionType.Or:
					if ((node.Type != typeof(bool)) && (node.Type != typeof(bool?)))
					{
						str = "|";
						innerPrecedence = ExpressionOperatorPrecedence.LogicalOR;
					}
					else
					{
						str = "Or";
						innerPrecedence = ExpressionOperatorPrecedence.LogicalOR;
					}
					break;

				case ExpressionType.OrElse:
					str = "||";
					innerPrecedence = ExpressionOperatorPrecedence.ConditionalOR;
					break;

				case ExpressionType.Power:
					str = "^";
					innerPrecedence = ExpressionOperatorPrecedence.LogicalXOR;
					break;

				case ExpressionType.RightShift:
					str = ">>";
					innerPrecedence = ExpressionOperatorPrecedence.Shift;
					break;

				case ExpressionType.Subtract:
					str = "-";
					innerPrecedence = ExpressionOperatorPrecedence.Additive;
					break;

				case ExpressionType.SubtractChecked:
					str = "-";
					innerPrecedence = ExpressionOperatorPrecedence.Additive;
					break;

				case ExpressionType.Assign:
					str = "=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.AddAssign:
					str = "+=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.AndAssign:
					if ((node.Type != typeof(bool)) && (node.Type != typeof(bool?)))
					{
						str = "&=";
						innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					}
					else
					{
						str = "&&=";
						innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					}
					break;

				case ExpressionType.DivideAssign:
					str = "/=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.ExclusiveOrAssign:
					str = "^=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.LeftShiftAssign:
					str = "<<=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.ModuloAssign:
					str = "%=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.MultiplyAssign:
					str = "*=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.OrAssign:
					if ((node.Type != typeof(bool)) && (node.Type != typeof(bool?)))
					{
						str = "|=";
						innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					}
					else
					{
						str = "||=";
						innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					}
					break;

				case ExpressionType.PowerAssign:
					str = "**=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.RightShiftAssign:
					str = ">>=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.SubtractAssign:
					str = "-=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.AddAssignChecked:
					str = "+=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.MultiplyAssignChecked:
					str = "*=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.SubtractAssignChecked:
					str = "-=";
					innerPrecedence = ExpressionOperatorPrecedence.Assignment;
					break;

				case ExpressionType.ArrayIndex:

					innerPrecedence = ExpressionOperatorPrecedence.Primary;

					SometimesParenthesis(outerPrecedence, innerPrecedence, delegate
					{
						Visit(leftOp, innerPrecedence);
						Out("[");
						Visit(rightOp, innerPrecedence);
						Out("]");
					});
					return node;

				default:
					throw new InvalidOperationException();
			}


			SometimesParenthesis(outerPrecedence, innerPrecedence, delegate
			{
				if (innerPrecedence == ExpressionOperatorPrecedence.NullCoalescing && TypeExistsOnServer(rightOp.Type))
				{
					Out("((");
					Out(ConvertTypeToCSharpKeyword(rightOp.Type));
					Out(")(");
				}
				Visit(leftOp, innerPrecedence);
				Out(' ');
				Out(str);
				Out(' ');
				Visit(rightOp, innerPrecedence);
				if (innerPrecedence == ExpressionOperatorPrecedence.NullCoalescing && TypeExistsOnServer(rightOp.Type))
				{
					Out("))");
				}
			});

			return node;
		}

		private void FixupEnumBinaryExpression(ref Expression left, ref Expression right)
		{
			switch (left.NodeType)
			{
				case ExpressionType.ConvertChecked:
				case ExpressionType.Convert:
					var expression = ((UnaryExpression)left).Operand;
					var enumType = Nullable.GetUnderlyingType(expression.Type) ?? expression.Type;
					if (enumType.IsEnum() == false)
						return;

					var constantExpression = SkipConvertExpressions(right) as ConstantExpression;
					if (constantExpression == null)
						return;
					left = expression;
					if (constantExpression.Value == null)
					{
						right = Expression.Constant(null);
					}
					else
					{
						right = convention.SaveEnumsAsIntegers
                                    ? Expression.Constant((int)constantExpression.Value)
									: Expression.Constant(Enum.ToObject(enumType, constantExpression.Value).ToString());

					}
				break;
			}

			while (true)
			{
				switch (left.NodeType)
				{
					case ExpressionType.ConvertChecked:
					case ExpressionType.Convert:
						left = ((UnaryExpression)left).Operand;
						break;
					default:
						return;
				}
			}
		}

		private Expression SkipConvertExpressions(Expression expression)
		{
			switch (expression.NodeType)
			{
				case ExpressionType.ConvertChecked:
				case ExpressionType.Convert:
                    return SkipConvertExpressions(((UnaryExpression)expression).Operand);
				default:
					return expression;
			}
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.BlockExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitBlock(BlockExpression node)
		{
			Out("{");
			foreach (var expression in node.Variables)
			{
				Out("var ");
				Visit(expression);
				Out(";");
			}
			Out(" ... }");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.CatchBlock" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override CatchBlock VisitCatchBlock(CatchBlock node)
		{
			Out("catch (" + node.Test.Name);
			if (node.Variable != null)
			{
				Out(node.Variable.Name);
			}
			Out(") { ... }");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.ConditionalExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitConditional(ConditionalExpression node)
		{
			return VisitConditional(node, _currentPrecedence);
		}

		private Expression VisitConditional(ConditionalExpression node, ExpressionOperatorPrecedence outerPrecedence)
		{
			const ExpressionOperatorPrecedence innerPrecedence = ExpressionOperatorPrecedence.Conditional;

			SometimesParenthesis(outerPrecedence, innerPrecedence, delegate
			{
				Visit(node.Test, innerPrecedence);
				Out(" ? ");
				Visit(node.IfTrue, innerPrecedence);
				Out(" : ");
				Visit(node.IfFalse, innerPrecedence);
			});

			return node;
		}

		/// <summary>
		///   Visits the <see cref = "T:System.Linq.Expressions.ConstantExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitConstant(ConstantExpression node)
		{
			if (node.Value == null)
			{
				// Avoid converting/casting the type, if it already converted/casted.
				if (IsLastOperatorIs('='))
					ConvertTypeToCSharpKeywordIncludeNullable(node.Type);
				Out("null");
				return node;
			}

			var s = Convert.ToString(node.Value, CultureInfo.InvariantCulture);
			if (node.Value is string)
			{
				Out("\"");
				OutLiteral(node.Value as string);
				Out("\"");
				return node;
			}
			if (node.Value is bool)
			{
				Out(node.Value.ToString().ToLower());
				return node;
			}
			if (node.Value is char)
			{
				Out("'");
				OutLiteral((char)node.Value);
				Out("'");
				return node;
			}
			if (node.Value is Enum)
			{
				var enumType = node.Value.GetType();
				if (TypeExistsOnServer(enumType))
				{
					Out(enumType.FullName.Replace("+", "."));
					Out('.');
					Out(s);
					return node;
				}
				if (convention.SaveEnumsAsIntegers)
					Out((Convert.ToInt32(node.Value)).ToString());
				else
				{
					Out('"');
					Out(node.Value.ToString());
					Out('"');
				}
				return node;
			}
			if (node.Value is decimal)
			{
				Out(s);
				Out('M');
				return node;
			}
			Out(s);
			return node;
		}

		private void OutLiteral(string value)
		{
			if (value.Length == 0)
				return;

			_out.Append(string.Concat(value.ToCharArray().Select(EscapeChar)));
		}

		private void OutLiteral(char c)
		{
			_out.Append(EscapeChar(c));
		}

		private static string EscapeChar(char c)
		{
			var index = Array.IndexOf(LiteralSymbolsToEscape, c);

			if (index != -1)
				return LiteralEscapedSymbols[index];

			if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && !char.IsSymbol(c) && !char.IsPunctuation(c))
				return @"\u" + ((int)c).ToString("x4");

#if NETFX_CORE
			return c.ToString();
#else
			return c.ToString(CultureInfo.InvariantCulture);
#endif
		}

		private bool IsLastOperatorIs(char s)
		{
			var i = _out.Length - 1;
			while (i >= 0 && i < _out.Length)
			{
				var lastChar = _out[i];
				if (lastChar != ' ')
				{
					return lastChar == s;
				}
				--i;
			}

			return false;
		}

		private void ConvertTypeToCSharpKeywordIncludeNullable(Type type)
		{
			var nonNullableType = Nullable.GetUnderlyingType(type);
			type = nonNullableType ?? type;
			var isNullableType = nonNullableType != null;


			// we only cast enums and types is mscorlib. We don't support anything else
			// because the VB compiler like to put converts all over the place, and include
			// types that we can't really support (only exists on the client)
			if (ShouldConvert(type) == false)
				return;

			Out("(");
			Out(ConvertTypeToCSharpKeyword(type));

			if (isNullableType && nonNullableType != typeof(Guid))
			{
				Out("?");
			}
			Out(")");
		}

		private string ConvertTypeToCSharpKeyword(Type type)
		{
			if (type.IsGenericType)
			{
				if (TypeExistsOnServer(type) == false)
					throw new InvalidOperationException("Cannot make use of type " + type + " because it is a generic type that doesn't exists on the server");
				var typeDefinition = type.GetGenericTypeDefinition();
				var sb = new StringBuilder(typeDefinition.FullName, 0)
				{
					Length = typeDefinition.FullName.IndexOf('`')
				};
				sb.Replace('+', '.');
				sb.Append("<");

				var arguments = type.GetGenericArguments();
				for (int i = 0; i < arguments.Length; i++)
				{
					if (i != 0)
						sb.Append(", ");
					sb.Append(ConvertTypeToCSharpKeyword(arguments[i]));
				}

				sb.Append(">");

				return sb.ToString();
			}

			if (type == typeof(string))
			{
				return "string";
			}
			if (type == typeof(Guid) || type == typeof(Guid?))
			{
				return "string"; // on the server, Guids are represented as strings
			}
			if (type == typeof(char))
			{
				return "char";
			}
			if (type == typeof(bool))
			{
				return "bool";
			}
			if (type == typeof(bool?))
			{
				return "bool?";
			}
			if (type == typeof(decimal))
			{
				return "decimal";
			}
			if (type == typeof(decimal?))
			{
				return "decimal?";
			}
			if (type == typeof(double))
			{
				return "double";
			}
			if (type == typeof(double?))
			{
				return "double?";
			}
			if (type == typeof(float))
			{
				return "float";
			}
			if (type == typeof(float?))
			{
				return "float?";
			}

			if (type == typeof(long))
			{
				return "long";
			}
			if (type == typeof(long?))
			{
				return "long?";
			}
			if (type == typeof(int))
			{
				return "int";
			}
			if (type == typeof(int?))
			{
				return "int?";
			}
			if (type == typeof(short))
			{
				return "short";
			}
			if (type == typeof(short?))
			{
				return "short?";
			}
			if (type == typeof(byte))
			{
				return "byte";
			}
			if (type == typeof(byte?))
			{
				return "byte?";
			}
			if (type.IsEnum)
			{
				return "string";
			}
			if (type.FullName == "System.Object")
			{
				return "object";
			}

			const string knownNamespace = "System";
			if (knownNamespace == type.Namespace)
				return type.Name;
			return type.FullName;
		}

		private bool TypeExistsOnServer(Type type)
		{
			if (type.Assembly() == typeof(object).Assembly()) // mscorlib
				return true;

			if (type.Assembly() == typeof(HashSet<>).Assembly()) // System.Core
				return true;

			if (type.Assembly() == typeof(RavenJObject).Assembly())
				return true;

			if (type.Assembly().FullName.StartsWith("Lucene.Net") &&
				type.Assembly().FullName.Contains("PublicKeyToken=85089178b9ac3181"))
				return true;

			return false;
		}

		/// <summary>
		///   Visits the <see cref = "T:System.Linq.Expressions.DebugInfoExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitDebugInfo(DebugInfoExpression node)
		{
			var s = string.Format(CultureInfo.CurrentCulture, "<DebugInfo({0}: {1}, {2}, {3}, {4})>",
								  new object[] { node.Document.FileName, node.StartLine, node.StartColumn, node.EndLine, node.EndColumn });
			Out(s);
			return node;
		}

		/// <summary>
		///   Visits the <see cref = "T:System.Linq.Expressions.DefaultExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitDefault(DefaultExpression node)
		{
			Out("default(");

			var nonNullable = Nullable.GetUnderlyingType(node.Type);
			Out(ConvertTypeToCSharpKeyword(nonNullable ?? node.Type));
			if (nonNullable != null && nonNullable != typeof(Guid))
				Out("?");

			Out(")");
			return node;
		}

#if !NETFX_CORE
		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.DynamicExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitDynamic(DynamicExpression node)
		{
			Out(FormatBinder(node.Binder));
			VisitExpressions('(', node.Arguments, ')');
			return node;
		}
#endif

		/// <summary>
		///   Visits the element init.
		/// </summary>
		/// <param name = "initializer">The initializer.</param>
		/// <returns></returns>
		protected override ElementInit VisitElementInit(ElementInit initializer)
		{
			Out(initializer.AddMethod.ToString());
			VisitExpressions('(', initializer.Arguments, ')');
			return initializer;
		}

		private void VisitExpressions<T>(char open, IEnumerable<T> expressions, char close) where T : Expression
		{
			Out(open);
			if (expressions != null)
			{
				var flag = true;
				foreach (var local in expressions)
				{
					if (flag)
					{
						flag = false;
					}
					else
					{
						Out(", ");
					}
					Visit(local, ExpressionOperatorPrecedence.ParenthesisNotNeeded);
				}
			}
			Out(close);
		}

		/// <summary>
		///   Visits the children of the extension expression.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitExtension(Expression node)
		{
			const BindingFlags bindingAttr = BindingFlags.ExactBinding | BindingFlags.Public | BindingFlags.Instance;
			if (node.GetType().GetMethod("ToString", bindingAttr, null, ReflectionUtils.EmptyTypes, null).DeclaringType !=
				typeof(Expression))
			{
				Out(node.ToString());
				return node;
			}
			Out("[");
			Out(node.NodeType == ExpressionType.Extension ? node.GetType().FullName : node.NodeType.ToString());
			Out("]");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.GotoExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitGoto(GotoExpression node)
		{
#if NETFX_CORE
			Out(node.Kind.ToString().ToLower());
#else
			Out(node.Kind.ToString().ToLower(CultureInfo.CurrentCulture));
#endif
			DumpLabel(node.Target);
			if (node.Value != null)
			{
				Out(" (");
				Visit(node.Value);
				Out(") ");
			}
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.IndexExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitIndex(IndexExpression node)
		{
			if (node.Object != null)
			{
				Visit(node.Object);
			}
			else
			{
				Out(node.Indexer.DeclaringType.Name);
			}
			if (node.Indexer != null)
			{
				Out(".");
				Out(node.Indexer.Name);
			}
			VisitExpressions('[', node.Arguments, ']');
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.InvocationExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitInvocation(InvocationExpression node)
		{
			Out("Invoke(");
			Visit(node.Expression);
			var num = 0;
			var count = node.Arguments.Count;
			while (num < count)
			{
				Out(", ");
				Visit(node.Arguments[num]);
				num++;
			}
			Out(")");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.LabelExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitLabel(LabelExpression node)
		{
			Out("{ ... } ");
			DumpLabel(node.Target);
			Out(":");
			return node;
		}

		/// <summary>
		///   Visits the lambda.
		/// </summary>
		/// <typeparam name = "T"></typeparam>
		/// <param name = "node">The node.</param>
		/// <returns></returns>
		protected override Expression VisitLambda<T>(Expression<T> node)
		{
			if (node.Parameters.Count == 1)
			{
				Visit(node.Parameters[0]);
			}
			else
			{
				VisitExpressions('(', node.Parameters, ')');
			}
			Out(" => ");
			var body = node.Body;
			if (castLambdas)
			{
				switch (body.NodeType)
				{
					case ExpressionType.Convert:
					case ExpressionType.ConvertChecked:
						break;
					default:
						body = Expression.Convert(body, body.Type);
						break;
				}
			}
			Visit(body);
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.ListInitExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitListInit(ListInitExpression node)
		{
			Visit(node.NewExpression);
			Out(" {");
			var num = 0;
			var count = node.Initializers.Count;
			while (num < count)
			{
				if (num > 0)
				{
					Out(", ");
				}
				Out("{");
				bool first = true;
				foreach (var expression in node.Initializers[num].Arguments)
				{
					if (first == false)
						Out(", ");
					first = false;
					Visit(expression);
				}
				Out("}");
				num++;
			}
			Out("}");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.LoopExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitLoop(LoopExpression node)
		{
			Out("loop { ... }");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.MemberExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitMember(MemberExpression node)
		{

            if (Nullable.GetUnderlyingType(node.Member.DeclaringType) != null)
            {
                switch (node.Member.Name)
			{
                    case "HasValue":
                        // we don't have nullable type on the server side, we just compare to null
                        Out("(");
				Visit(node.Expression);
                        Out(" != null)");
                        return node;
                    case "Value":
                        Visit(node.Expression);
				return node; // we don't have nullable type on the server side, we can safely ignore this.
			}
            }

			OutMember(node.Expression, node.Member, node.Expression == null ? node.Type : node.Expression.Type);
			return node;
		}

		/// <summary>
		///   Visits the member assignment.
		/// </summary>
		/// <param name = "assignment">The assignment.</param>
		/// <returns></returns>
		protected override MemberAssignment VisitMemberAssignment(MemberAssignment assignment)
		{
			Out(assignment.Member.Name);
			Out(" = ");
			var constantExpression = assignment.Expression as ConstantExpression;
			if (constantExpression != null && constantExpression.Value == null)
			{
				var memberType = GetMemberType(assignment.Member);
				if (ShouldConvert(memberType))
				{
					Visit(Expression.Convert(assignment.Expression, memberType));
				}
				else
				{
					Out("(object)null");
				}
				return assignment;
			}
			Visit(assignment.Expression);
			return assignment;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.MemberInitExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitMemberInit(MemberInitExpression node)
		{
			if ((node.NewExpression.Arguments.Count == 0) && node.NewExpression.Type.Name.Contains("<"))
			{
				Out("new");
			}
			else
			{
				Visit(node.NewExpression);
				if (TypeExistsOnServer(node.Type) == false)
				{
					const int removeLength = 2;
					_out.Remove(_out.Length - removeLength, removeLength);
				}
			}
			Out(" {");
			var num = 0;
			var count = node.Bindings.Count;
			while (num < count)
			{
				var binding = node.Bindings[num];
				if (num > 0)
				{
					Out(", ");
				}
				VisitMemberBinding(binding);
				num++;
			}
			Out("}");
			return node;
		}

		/// <summary>
		///   Visits the member list binding.
		/// </summary>
		/// <param name = "binding">The binding.</param>
		/// <returns></returns>
		protected override MemberListBinding VisitMemberListBinding(MemberListBinding binding)
		{
			Out(binding.Member.Name);
			Out(" = {");
			var num = 0;
			var count = binding.Initializers.Count;
			while (num < count)
			{
				if (num > 0)
				{
					Out(", ");
				}
				VisitElementInit(binding.Initializers[num]);
				num++;
			}
			Out("}");
			return binding;
		}

		/// <summary>
		///   Visits the member member binding.
		/// </summary>
		/// <param name = "binding">The binding.</param>
		/// <returns></returns>
		protected override MemberMemberBinding VisitMemberMemberBinding(MemberMemberBinding binding)
		{
			Out(binding.Member.Name);
			Out(" = {");
			var num = 0;
			var count = binding.Bindings.Count;
			while (num < count)
			{
				if (num > 0)
				{
					Out(", ");
				}
				VisitMemberBinding(binding.Bindings[num]);
				num++;
			}
			Out("}");
			return binding;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.MethodCallExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitMethodCall(MethodCallExpression node)
		{
			var constantExpression = node.Object as ConstantExpression;
			if (constantExpression != null && node.Type == typeof(Delegate))
			{
				var methodInfo = constantExpression.Value as MethodInfo;
				if (methodInfo != null && methodInfo.DeclaringType == typeof(AbstractCommonApiForIndexesAndTransformers))// a delegate call
				{
					Out("((Func<");
					for (int i = 0; i < methodInfo.GetParameters().Length; i++)
					{
						Out("dynamic, ");
					}
					Out("dynamic>)(");
					Out(methodInfo.Name);
					Out("))");
					return node;
				}
			}
            if (node.Method.Name == "GetValueOrDefault" && Nullable.GetUnderlyingType(node.Method.DeclaringType) != null)
            {
                Visit(node.Object);
                return node; // we don't do anything here on the server
            }

			var num = 0;
			var expression = node.Object;
			if (IsExtensionMethod(node))
			{
				num = 1;
				expression = node.Arguments[0];
			}
			if (expression != null)
			{
				switch (node.Method.Name)
				{
					case "MetadataFor":
						Visit(node.Arguments[0]);
						Out("[\"@metadata\"]");
						return node;
					case "AsDocument":
						Visit(node.Arguments[0]);
						return node;
				}
				if (expression.Type == typeof(IClientSideDatabase))
				{
					Out("Database");
				}
				else if (typeof(AbstractCommonApiForIndexesAndTransformers).IsAssignableFrom(expression.Type))
				{
					// this is a method that
					// exists on both the server side and the client side
					Out("this");
				}
                else
				{
					Visit(expression);
				}
				if (IsIndexerCall(node) == false)
				{
					Out(".");
				}
			}
			if (node.Method.IsStatic && ShouldConvertToDynamicEnumerable(node))
			{
				Out("DynamicEnumerable.");
			}
			else if (node.Method.IsStatic && IsExtensionMethod(node) == false)
			{
				if (node.Method.DeclaringType == typeof(Enumerable) && node.Method.Name == "Cast")
				{
					Out("new Raven.Abstractions.Linq.DynamicList(");
					Visit(node.Arguments[0]);
					Out(")");
					return node; // we don't do casting on the server
				}
				Out(node.Method.DeclaringType.Name);
				Out(".");
			}
			if (IsIndexerCall(node))
			{
				Out("[");
			}
			else
			{
				switch (node.Method.Name)
				{
					case "First":
						Out("FirstOrDefault");
						break;
					case "Last":
						Out("LastOrDefault");
						break;
					case "Single":
						Out("SingleOrDefault");
						break;
					case "ElementAt":
						Out("ElementAtOrDefault");
						break;
					// Convert OfType<Foo>() to Where(x => x["$type"] == typeof(Foo).AssemblyQualifiedName)
					case "OfType":
						Out("Where");
						break;
					default:
						Out(node.Method.Name);
                        if (node.Method.IsGenericMethod)
                        {
                            OutputGenericMethodArgumentsIfNeeded(node.Method);
                        }
						break;
				}
				Out("(");
			}
			var num2 = num;
			var count = node.Arguments.Count;
			while (num2 < count)
			{
				if (num2 > num)
				{
					Out(", ");
				}
				var old = castLambdas;
				try
				{
					switch (node.Method.Name)
					{
						case "Sum":
						case "Average":
						case "Min":
						case "Max":
							castLambdas = true;
							break;
						default:
							castLambdas = false;
							break;
					}
					Visit(node.Arguments[num2]);

					// Convert OfType<Foo>() to Where(x => x["$type"] == typeof(Foo).AssemblyQualifiedName)
					if (node.Method.Name == "OfType")
					{
						var type = node.Method.GetGenericArguments()[0];
						var typeFullName = ReflectionUtil.GetFullNameWithoutVersionInformation(type);
						Out(", (Func<dynamic, bool>)(_itemRaven => string.Equals(_itemRaven[\"$type\"], \"");
						Out(typeFullName);
						Out("\", StringComparison.Ordinal))");
					}
				}
				finally
				{
					castLambdas = old;
				}
				num2++;
			}
			Out(IsIndexerCall(node) ? "]" : ")");

			if (node.Type.IsValueType() && TypeExistsOnServer(node.Type))
			{
				switch (node.Method.Name)
				{
					case "First":
					case "FirstOrDefault":
					case "Last":
					case "LastOrDefault":
					case "Single":
					case "SingleOrDefault":
					case "ElementAt":
					case "ElementAtOrDefault":
						Out(" ?? ");
						VisitDefault(Expression.Default(node.Type));
						break;
				}
			}
			return node;
		}

        private void OutputGenericMethodArgumentsIfNeeded(MethodInfo method)
        {
            var genericArguments = method.GetGenericArguments();
            if (genericArguments.All(TypeExistsOnServer) == false)
                return; // no point if the types aren't on the server
            switch (method.DeclaringType.Name)
            {
                case "Enumerable":
                case "Queryable":
                    return; // we don't need thos, we have LinqOnDynamic for it
            }

            Out("<");
            bool first = true;
            foreach (var genericArgument in genericArguments)
            {
                if (first == false)
                {
                    Out(", ");
                }
                first = false;

                VisitType(genericArgument);
            }
            Out(">");
        }

		private static bool IsIndexerCall(MethodCallExpression node)
		{
			return node.Method.IsSpecialName && (node.Method.Name.StartsWith("get_") || node.Method.Name.StartsWith("set_"));
		}

		private static bool ShouldConvertToDynamicEnumerable(MethodCallExpression node)
		{
			if (node.Method.DeclaringType.Name == "Enumerable")
			{
				switch (node.Method.Name)
				{
					case "First":
					case "FirstOrDefault":
					case "Single":
					case "SingleOrDefault":
					case "Last":
					case "LastOrDefault":
					case "ElementAt":
					case "ElementAtOrDefault":
					case "Min":
					case "Max":
					case "Union":
						return true;
				}
			}

			return false;
		}
		private static bool IsExtensionMethod(MethodCallExpression node)
		{
#if NETFX_CORE
			var attribute = node.Method.GetType().GetTypeInfo().GetCustomAttribute(typeof (ExtensionAttribute));
#else
			var attribute = Attribute.GetCustomAttribute(node.Method, typeof(ExtensionAttribute));
#endif
			if (attribute == null)
				return false;

			if (node.Method.DeclaringType.Name == "Enumerable")
			{
				switch (node.Method.Name)
				{
					case "Select":
					case "SelectMany":
					case "Where":
					case "GroupBy":
					case "OrderBy":
					case "OrderByDescending":
					case "DefaultIfEmpty":
					case "Reverse":
						return true;
				}
				return false;
			}

			if (node.Method.GetCustomAttributes(typeof(RavenMethodAttribute), false).Length != 0)
				return false;

			return true;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.NewExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitNew(NewExpression node)
		{
			Out("new ");
			if (TypeExistsOnServer(node.Type))
			{
				VisitType(node.Type);
				Out("(");
			}
			else
			{
				Out("{");
			}
			for (var i = 0; i < node.Arguments.Count; i++)
			{
				if (i > 0)
				{
					Out(", ");
				}
				if (node.Members != null && node.Members[i] != null)
				{
					Out(node.Members[i].Name);
					Out(" = ");

					var constantExpression = node.Arguments[i] as ConstantExpression;
					if (constantExpression != null && constantExpression.Value == null)
					{
						Out("(");
						VisitType(GetMemberType(node.Members[i]));
						Out(")");
					}
				}

				Visit(node.Arguments[i]);
			}
			if (TypeExistsOnServer(node.Type))
			{
				Out(")");
			}
			else
			{
				Out("}");
			}
			return node;
		}

		private void VisitType(Type type)
		{
			if (type.IsGenericType() == false || CheckIfAnonymousType(type))
			{
				if (type.IsArray)
				{
					VisitType(type.GetElementType());
					Out("[");
					for (int i = 0; i < type.GetArrayRank() - 1; i++)
					{
						Out(",");
					}
					Out("]");
					return;
				}
				var nonNullableType = Nullable.GetUnderlyingType(type);
				if (nonNullableType != null)
				{
					VisitType(nonNullableType);
					Out("?");
					return;
				}
				Out(ConvertTypeToCSharpKeyword(type));
				return;
			}
			var genericArguments = type.GetGenericArguments();
			var genericTypeDefinition = type.GetGenericTypeDefinition();
			var lastIndexOfTag = genericTypeDefinition.FullName.LastIndexOf('`');

			Out(genericTypeDefinition.FullName.Substring(0, lastIndexOfTag));
			Out("<");
			bool first = true;
			foreach (var genericArgument in genericArguments)
			{
				if (first == false)
					Out(", ");
				first = false;
				VisitType(genericArgument);
			}
			Out(">");
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.NewArrayExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitNewArray(NewArrayExpression node)
		{
			switch (node.NodeType)
			{
				case ExpressionType.NewArrayInit:
					Out("new ");
					if (!CheckIfAnonymousType(node.Type.GetElementType()) && TypeExistsOnServer(node.Type.GetElementType()))
					{
						Out(ConvertTypeToCSharpKeyword(node.Type.GetElementType()));
					}
					else if (node.Expressions.Count == 0)
					{
						Out("object");
					}
					Out("[]");
					VisitExpressions('{', node.Expressions, '}');
					return node;

				case ExpressionType.NewArrayBounds:
					Out("new " + node.Type);
					VisitExpressions('(', node.Expressions, ')');
					return node;
			}
			return node;
		}

		private static bool CheckIfAnonymousType(Type type)
		{
			// hack: the only way to detect anonymous types right now
			return type.IsDefined(typeof(CompilerGeneratedAttribute), false)
				&& type.IsGenericType() && type.Name.Contains("AnonymousType")
				&& (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
				&& type.GetTypeInfo().Attributes.HasFlag(TypeAttributes.NotPublic);
		}

		public static readonly HashSet<string> keywordsInCSharp = new HashSet<string>(new[]
		{
			"abstract",
			"as",
			"base",
			"bool",
			"break",
			"byte",
			"case",
			"catch",
			"char",
			"checked",
			"class",
			"const",
			"continue",
			"decimal",
			"default",
			"delegate",
			"do",
			"double",
			"else",
			"enum",
			"event",
			"explicit",
			"extern",
			"false",
			"finally",
			"fixed",
			"float",
			"for",
			"foreach",
			"goto",
			"if",
			"implicit",
			"in",
			"in (generic modifier)",
			"int",
			"interface",
			"internal",
			"is",
			"lock",
			"long",
			"namespace",
			"new",
			"null",
			"object",
			"operator",
			"out",
			"out (generic modifier)",
			"override",
			"params",
			"private",
			"protected",
			"public",
			"readonly",
			"ref",
			"return",
			"sbyte",
			"sealed",
			"short",
			"sizeof",
			"stackalloc",
			"static",
			"string",
			"struct",
			"switch",
			"this",
			"throw",
			"true",
			"try",
			"typeof",
			"uint",
			"ulong",
			"unchecked",
			"unsafe",
			"ushort",
			"using",
			"virtual",
			"void",
			"volatile",
			"while"
		});

		/// <summary>
		///   Visits the <see cref = "T:System.Linq.Expressions.ParameterExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitParameter(ParameterExpression node)
		{
			if (node.IsByRef)
			{
				Out("ref ");
			}
			if (string.IsNullOrEmpty(node.Name))
			{
				Out("Param_" + GetParamId(node));
				return node;
			}
			var name = node.Name.StartsWith("$VB$") ? node.Name.Substring(4) : node.Name;
			if (keywordsInCSharp.Contains(name))
				Out('@');
			Out(name);
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.RuntimeVariablesExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
		{
			VisitExpressions('(', node.Variables, ')');
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.SwitchExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitSwitch(SwitchExpression node)
		{
			Out("switch ");
			Out("(");
			Visit(node.SwitchValue);
			Out(") { ... }");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.SwitchCase" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override SwitchCase VisitSwitchCase(SwitchCase node)
		{
			Out("case ");
			VisitExpressions('(', node.TestValues, ')');
			Out(": ...");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.TryExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitTry(TryExpression node)
		{
			Out("try { ... }");
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.TypeBinaryExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitTypeBinary(TypeBinaryExpression node)
		{
			const ExpressionOperatorPrecedence currentPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
			string op;
			switch (node.NodeType)
			{
				case ExpressionType.TypeIs:
					op = " is ";
					break;

				case ExpressionType.TypeEqual:
					op = " TypeEqual ";
					break;
				default:
					throw new InvalidOperationException();
			}


			Visit(node.Expression, currentPrecedence);
			Out(op);
			Out(node.TypeOperand.Name);
			return node;
		}

		/// <summary>
		///   Visits the children of the <see cref = "T:System.Linq.Expressions.UnaryExpression" />.
		/// </summary>
		/// <param name = "node">The expression to visit.</param>
		/// <returns>
		///   The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
		/// </returns>
		protected override Expression VisitUnary(UnaryExpression node)
		{
			return VisitUnary(node, _currentPrecedence);
		}

		private Expression VisitUnary(UnaryExpression node, ExpressionOperatorPrecedence outerPrecedence)
		{
			var innerPrecedence = ExpressionOperatorPrecedence.Unary;

			switch (node.NodeType)
			{
				case ExpressionType.TypeAs:
					innerPrecedence = ExpressionOperatorPrecedence.RelationalAndTypeTesting;
					break;

				case ExpressionType.Decrement:
					innerPrecedence = ExpressionOperatorPrecedence.ParenthesisNotNeeded;
					Out("Decrement(");
					break;

				case ExpressionType.Negate:
				case ExpressionType.NegateChecked:
					Out("-");
					break;

				case ExpressionType.UnaryPlus:
					Out("+");
					break;

				case ExpressionType.Not:
					Out("!");
					break;

				case ExpressionType.Quote:
					break;

				case ExpressionType.Increment:
					innerPrecedence = ExpressionOperatorPrecedence.ParenthesisNotNeeded;
					Out("Increment(");
					break;

				case ExpressionType.Throw:
					innerPrecedence = ExpressionOperatorPrecedence.ParenthesisNotNeeded;
					Out("throw ");
					break;

				case ExpressionType.PreIncrementAssign:
					Out("++");
					break;

				case ExpressionType.PreDecrementAssign:
					Out("--");
					break;

				case ExpressionType.OnesComplement:
					Out("~");
					break;
				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
                    if (node.Method != null && node.Method.Name == "Parse" && node.Method.DeclaringType == typeof(DateTime))
                    {
                        Out(node.Method.DeclaringType.Name);
                        Out(".Parse(");
                    }
                    else
                    {
					Out("(");
					ConvertTypeToCSharpKeywordIncludeNullable(node.Type);
                    }
					break;
				case ExpressionType.ArrayLength:
					// we don't want to do nothing for those
					Out("(");
					break;
				default:
					innerPrecedence = ExpressionOperatorPrecedence.ParenthesisNotNeeded;
					Out(node.NodeType.ToString());
					Out("(");
					break;
			}

			SometimesParenthesis(outerPrecedence, innerPrecedence, () => Visit(node.Operand, innerPrecedence));

			switch (node.NodeType)
			{
				case ExpressionType.TypeAs:
					Out(" As ");
					Out(node.Type.Name);
					break;

				case ExpressionType.ArrayLength:
					Out(".Length)");
					break;

				case ExpressionType.Decrement:
				case ExpressionType.Increment:
					Out(")");
					break;

				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
					Out(")");
					break;

				case ExpressionType.Negate:
				case ExpressionType.UnaryPlus:
				case ExpressionType.NegateChecked:
				case ExpressionType.Not:
				case ExpressionType.Quote:
				case ExpressionType.Throw:
				case ExpressionType.PreIncrementAssign:
				case ExpressionType.PreDecrementAssign:
				case ExpressionType.OnesComplement:
					break;

				case ExpressionType.PostIncrementAssign:
					Out("++");
					break;

				case ExpressionType.PostDecrementAssign:
					Out("--");
					break;

				default:
					Out(")");
					break;
			}

			return node;
		}

		private static bool ShouldConvert(Type nonNullableType)
		{
			if (nonNullableType.IsEnum())
				return true;

			return nonNullableType.Assembly() == typeof(string).Assembly() && (nonNullableType.IsGenericType() == false);
		}
	}
}

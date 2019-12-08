﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace ObjectPrinting
{
    public class PrintingConfig<TOwner> : IPrintingConfig
    {
        private readonly HashSet<Type> excludedTypes = new HashSet<Type>();
        private readonly HashSet<PropertyInfo> excludedProperties = new HashSet<PropertyInfo>();

        private readonly Dictionary<Type, Func<object, string>> alternatePropertySerialisatorByType =
            new Dictionary<Type, Func<object, string>>();

        private readonly Dictionary<Type, Func<object, string>> cultureInfoApplierByType =
            new Dictionary<Type, Func<object, string>>();

        private readonly Dictionary<PropertyInfo, Func<object, string>> individualSetUpFuncByPropertyInfo =
            new Dictionary<PropertyInfo, Func<object, string>>();

        private readonly Dictionary<PropertyInfo, int> maxValueLengthByPropertyInfo =
            new Dictionary<PropertyInfo, int>();

        private readonly int serialiseDepth;
        private readonly int sequencesMaxLength;
        private int mutualMaxPropertiesLength;
        private PropertyInfo currentSettingUpProperty;

        public PrintingConfig(int serialiseDepth, int sequencesMaxLength)
        {
            this.serialiseDepth = serialiseDepth;
            this.sequencesMaxLength = sequencesMaxLength;
        }

        void IPrintingConfig.SetCultureInfoApplierForNumberType<TNumber>(Func<TNumber, string> cultureInfoApplier)
        {
            if (currentSettingUpProperty is null)
                cultureInfoApplierByType[typeof(TNumber)] = ApplyCultureInfo;
            else
                individualSetUpFuncByPropertyInfo[currentSettingUpProperty] = ApplyCultureInfo;

            string ApplyCultureInfo(object number) => cultureInfoApplier((TNumber)number);
        }

        void IPrintingConfig.SetMaxValueLengthForStringProperty(int maxValueLength)
        {
            if (currentSettingUpProperty is null)
                mutualMaxPropertiesLength = maxValueLength;
            else
                maxValueLengthByPropertyInfo[currentSettingUpProperty] = maxValueLength;
        }

        void IPrintingConfig.SetAlternatePropertySerialisator<TPropType>(Func<TPropType, string> alternateSerialisator)
        {
            if (currentSettingUpProperty is null)
                alternatePropertySerialisatorByType[typeof(TPropType)] = SerialiseProperty;
            else
                individualSetUpFuncByPropertyInfo[currentSettingUpProperty] = SerialiseProperty;

            string SerialiseProperty(object propertyValue) => alternateSerialisator((TPropType)propertyValue);
        }

        public PropertyPrintingConfig<TOwner, TPropType> Printing<TPropType>()
        {
            currentSettingUpProperty = null;

            return new PropertyPrintingConfig<TOwner, TPropType>(this);
        }

        public PropertyPrintingConfig<TOwner, TPropType> Printing<TPropType>(
            Expression<Func<TOwner, TPropType>> memberSelector)
        {
            currentSettingUpProperty = GetPropertyInfoFromMemberExpression(memberSelector);

            return new PropertyPrintingConfig<TOwner, TPropType>(this);
        }

        public PrintingConfig<TOwner> Excluding<TPropType>(Expression<Func<TOwner, TPropType>> memberSelector)
        {
            var propertyInfo = GetPropertyInfoFromMemberExpression(memberSelector);

            excludedProperties.Add(propertyInfo);

            return this;
        }

        public PrintingConfig<TOwner> Excluding<TPropType>()
        {
            excludedTypes.Add(typeof(TPropType));

            return this;
        }

        private static PropertyInfo GetPropertyInfoFromMemberExpression<TPropType>(
            Expression<Func<TOwner, TPropType>> memberSelector)
        {
            if (!(memberSelector.Body is MemberExpression memberExpression) ||
                !(memberExpression.Member is PropertyInfo propertyInfo))
                throw new ArgumentException("Passed expression has to represent accessing a property",
                                            nameof(memberSelector));
            return propertyInfo;
        }

        public string PrintToString(TOwner printedObject) => PrintToString(printedObject, 0);

        private string PrintToString(object printedObject, int nestingLevel)
        {
            if (printedObject is null)
                return PrintingConfigHelper.NullRepresentation + Environment.NewLine;

            var objectRuntimeType = printedObject.GetType();

            if (excludedTypes.Contains(objectRuntimeType))
                return string.Empty;

            if (nestingLevel > serialiseDepth)
                throw new ApplicationException(
                    $"Was detected nesting level more than specified serialiseDepth ({serialiseDepth})");

            if (alternatePropertySerialisatorByType.TryGetValue(objectRuntimeType, out var alternateSerialisator))
                return alternateSerialisator(printedObject) + Environment.NewLine;

            if (cultureInfoApplierByType.TryGetValue(objectRuntimeType, out var cultureInfoApplier))
                return cultureInfoApplier(printedObject) + Environment.NewLine;

            if (PrintingConfigHelper.IsFinalType(objectRuntimeType))
                return printedObject + Environment.NewLine;

            switch (printedObject)
            {
                case IDictionary dictionary:
                    return SerialiseDictionary(dictionary, nestingLevel);
                case IEnumerable enumerable:
                    return SerialiseEnumerable(enumerable, nestingLevel);
            }

            var objectSerialisationBuilder = new StringBuilder();

            objectSerialisationBuilder.AppendLine(objectRuntimeType.Name);

            objectSerialisationBuilder.Append(PrintAllProperties(printedObject, objectRuntimeType, nestingLevel));

            return objectSerialisationBuilder.ToString();
        }

        private string PrintAllProperties(object printedObject, Type objectRuntimeType, int nestingLevel)
        {
            var propertiesBuilder = new StringBuilder();
            var indentation = new string(PrintingConfigHelper.Indentation, nestingLevel + 1);

            foreach (var propertyInfo in objectRuntimeType.GetProperties().Where(pInfo => !IsExcludedProperty(pInfo)))
            {
                var propertyValue = propertyInfo.GetValue(printedObject);

                var propertyValueSerialisation = SerialisePropertyValue(propertyInfo, propertyValue);

                propertyValueSerialisation = TrimValueIfNecessary(propertyInfo, propertyValueSerialisation);

                var propertySerialisation = string.Concat(indentation,
                                                          propertyInfo.Name,
                                                          " = ",
                                                          propertyValueSerialisation);

                propertiesBuilder.Append(propertySerialisation);
            }

            return propertiesBuilder.ToString();

            string SerialisePropertyValue(PropertyInfo propertyInfo, object propertyValue) =>
                individualSetUpFuncByPropertyInfo.TryGetValue(propertyInfo, out var setUpFunc)
                    ? setUpFunc(propertyValue) + Environment.NewLine
                    : PrintToString(propertyValue, nestingLevel + 1);
        }

        private bool IsExcludedProperty(PropertyInfo propertyInfo) =>
            excludedTypes.Contains(propertyInfo.PropertyType) ||
            excludedProperties.Contains(propertyInfo);

        private string TrimValueIfNecessary(PropertyInfo propertyInfo, string propertyValueSerialisation)
        {
            if (propertyInfo.PropertyType != typeof(string)) return propertyValueSerialisation;

            var maxPropertyLength = maxValueLengthByPropertyInfo.TryGetValue(propertyInfo, out var maxValueLength)
                                        ? maxValueLength
                                        : mutualMaxPropertiesLength;

            if (maxPropertyLength > 0)
                return TruncateString(propertyValueSerialisation, maxPropertyLength) +
                       $"...{Environment.NewLine}";

            return propertyValueSerialisation;
        }

        private static string TruncateString(string str, int maxLength) =>
            string.IsNullOrEmpty(str) ? str : str.Substring(0, Math.Min(str.Length, maxLength));

        // ReSharper disable once SuggestBaseTypeForParameter
        private string SerialiseDictionary(IDictionary dictionary, int nestingLevel)
        {
            var dictionaryBuilder = new StringBuilder();

            if (dictionary.Count > sequencesMaxLength)
                throw new ApplicationException($@"Was detected sequence with length ({dictionary.Count
                                                   }) more than specified sequencesMaxLength ({sequencesMaxLength})");

            foreach (DictionaryEntry entry in dictionary)
            {
                var key = PrintToString(entry.Key, nestingLevel).TrimLineTerminator();
                var value = PrintToString(entry.Value, nestingLevel).TrimLineTerminator();

                key = WrapIfCollection(key, entry.Key);
                value = WrapIfCollection(value, entry.Value);

                dictionaryBuilder.Append($"[{key}]: {value} ");
            }

            dictionaryBuilder.Remove(dictionaryBuilder.Length - 1, 1); // removed redundant whitespace
            dictionaryBuilder.AppendLine();

            return dictionaryBuilder.ToString();
        }

        private string SerialiseEnumerable(IEnumerable enumerable, int nestingLevel) =>
            string.Join(' ', enumerable.Cast<object>()
                            .Select((obj, index) =>
                            {
                                if (index > sequencesMaxLength)
                                    throw new ApplicationException(
                                        $@"Was detected sequence with length more than specified sequencesMaxLength ({
                                            sequencesMaxLength})");

                                return WrapIfCollection(PrintToString(obj, nestingLevel).TrimLineTerminator(), obj);
                            }))
          + Environment.NewLine;

        private static string WrapIfCollection(string objectSerialisation, object wrappedObject)
        {
            var objectType = wrappedObject.GetType();

            return wrappedObject is IEnumerable && !PrintingConfigHelper.IsFinalType(objectType)
                       ? $"{objectType}({objectSerialisation})"
                       : objectSerialisation;
        }
    }
}
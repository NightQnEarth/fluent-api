﻿using System;

namespace ObjectPrinting
{
    public class PropertyPrintingConfig<TOwner, TPropType> : IPropertyPrintingConfig<TOwner, TPropType>
    {
        private readonly PrintingConfig<TOwner> printingConfig;

        public PropertyPrintingConfig(PrintingConfig<TOwner> printingConfig) => this.printingConfig = printingConfig;

        PrintingConfig<TOwner> IPropertyPrintingConfig<TOwner, TPropType>.ParentConfig => printingConfig;

        public PrintingConfig<TOwner> Using(Func<TPropType, string> print)
        {
            ((IPrintingConfig)printingConfig).SetAlternatePropertySerialisator(print);

            return printingConfig;
        }
    }
}
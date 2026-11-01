namespace GeoPulse.Core.Models
{
    /// <summary>Скважина/ствол: справочная информация из файла добычи.</summary>
    public class Well
    {
        /// <summary>Идентификатор (PK).</summary>
        public int Id { get; set; }

        /// <summary>Код ствола в НПД (NPD_WELL_BORE_CODE).</summary>
        public int NpdWellBoreCode { get; set; }

        /// <summary>Имя ствола (NPD_WELL_BORE_NAME / WELL_BORE_CODE).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Месторождение (NPD_FIELD_NAME).</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Тип скважины (WELL_TYPE: OP / WI).</summary>
        public string Type { get; set; } = string.Empty;
    }
}

using System;
using System.IO;

namespace dvbapiNet.Dvb.Descriptors
{
    /// <summary>
    /// IMplementiert den generischen DVB Descriptor
    /// </summary>
    public class DescriptorBase
    {
        protected byte[] _Data;
        protected DescriptorTag _DescTag;

        /// <summary>
        /// Länge des Descriptors in Bytes inkl. Tag- und Längenbyte.
        /// </summary>
        public virtual int Length
        {
            get
            {
                if (_Data == null)
                    return -1;

                return _Data.Length + 2;
            }
        }

        /// <summary>
        /// Gibt den Typ des Descriptors an.
        /// </summary>
        public DescriptorTag Tag
        {
            get
            {
                return _DescTag;
            }
        }

        public DescriptorBase(byte[] data, int offset)
        {
            _DescTag = (DescriptorTag)data[0 + offset];

            int declared = data[offset + 1];

            // Gegen abgeschnittene Daten absichern. SectionBase prüft die Vollständigkeit
            // einer Section, nicht ob das Längenbyte jedes Descriptors in den Section-Loop
            // passt. Ein zu großes Längenbyte würde sonst Array.Copy zum Werfen bringen und
            // damit das komplette Parsing der Section abbrechen.
            int available = data.Length - offset - 2;

            if (declared > available)
                declared = available;

            if (declared < 0)
                declared = 0;

            _Data = new byte[declared];
            Array.Copy(data, offset + 2, _Data, 0, _Data.Length);
        }

        public DescriptorBase(DescriptorTag tag)
        {
            _DescTag = tag;
        }

        public virtual void Write(Stream s)
        {
            if (_Data == null)
                throw new NotImplementedException("Diese Funktion muss von einer abgeleiteten Klasse implementiert werden, wenn keine Daten hinterlegt sind.");

            s.WriteByte((byte)_DescTag);
            s.WriteByte((byte)_Data.Length);

            s.Write(_Data, 0, _Data.Length);
        }
    }
}

using Xunit;

// La configuración de pruebas usa variables de entorno para aislar SQLite y el
// directorio de uploads. No ejecutar clases de prueba en paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

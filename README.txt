OPTIMIZADOR MAESTRO v1.0 — C# .NET 8
======================================

COMO GENERAR EL EXE (una sola vez)
------------------------------------

1. Descarga e instala el .NET 8 SDK (gratis, de Microsoft):
   https://dotnet.microsoft.com/download/dotnet/8.0
   
   -> Elige: ".NET 8 SDK" (el de mayor version)
   -> Arquitectura: x64
   -> NO el Runtime, el SDK completo

2. Haz doble clic en:  COMPILAR.bat

3. Espera ~1-2 minutos (descarga paquetes la primera vez)

4. El archivo  OptimizadorMaestro.exe  aparece en esta carpeta.

5. Copia ese .exe a cualquier PC con Windows 10/11 y funciona solo.
   No necesita instalar .NET ni nada. Todo va dentro del EXE.


ESTRUCTURA DE ARCHIVOS
-----------------------
  Program.cs                  <- Codigo fuente completo
  OptimizadorMaestro.csproj   <- Configuracion del proyecto
  app.manifest                <- Solicitar privilegios Admin
  COMPILAR.bat                <- Script de compilacion
  README.txt                  <- Este archivo


TAMANIO ESPERADO DEL EXE
--------------------------
  ~12-15 MB (incluye el runtime .NET 8 completo dentro)


REQUISITOS PARA COMPILAR
--------------------------
  - Windows 10/11
  - .NET 8 SDK instalado
  - Conexion a internet (solo la primera vez, para descargar NuGet)


REQUISITOS PARA EJECUTAR EL EXE GENERADO
------------------------------------------
  - Windows 10/11 x64
  - Nada mas. El EXE es completamente autonomo.


NOTAS
------
  El boton "Aplicar Mejoras" todavia no ejecuta los tweaks reales
  (muestra un mensaje de confirmacion pero no hace cambios).
  
  La logica real de cada tweak se agrega en OnApply() dentro de Program.cs,
  en la seccion marcada con:  // TODO: logica real de tweaks aqui

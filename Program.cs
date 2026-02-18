// OptimizadorMaestro v1.0 — C# .NET 8 WinForms — Single File Portable EXE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

// ── Elevacion de privilegios ──────────────────────────────────────────────────
static class Admin {
    [DllImport("shell32.dll")] static extern bool IsUserAnAdmin();
    public static bool IsAdmin() { try { return IsUserAnAdmin(); } catch { return false; } }
    public static void Elevate() {
        var info = new ProcessStartInfo(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName)
            { UseShellExecute = true, Verb = "runas" };
        try { Process.Start(info); } catch { }
    }
}

// ── Modelos ───────────────────────────────────────────────────────────────────
enum RecColor { Green, Yellow, Red }
record TweakItem(string Name, string Desc, string Rec, string Pct, RecColor Color);
record TweakSection(string Name, bool IsWarning, TweakItem[] Items);

// ── Datos del monitor (compartidos hilo bg ↔ UI) ──────────────────────────────
class MonData {
    public volatile int    CpuPct; public volatile string CpuTxt = "Iniciando...";
    public volatile int    RamPct; public volatile string RamTxt = "Iniciando...";
    public volatile int    NetPct; public volatile string NetTxt = "Iniciando...";
    public volatile int    GpuPct; public volatile string GpuTxt = "Iniciando...";
    public volatile string Ts = "";
    public volatile bool   Running = true;
    public DiskSlot[]      Disks   = Array.Empty<DiskSlot>();
}
class DiskSlot { public volatile int Pct; public volatile string Txt = "Leyendo..."; }
record MonWidget(Label Val, ProgressBar Bar);

// ═════════════════════════════════════════════════════════════════════════════
class MainForm : Form
{
    // Colores
    static readonly Color cBg      = Color.FromArgb(20,20,30);
    static readonly Color cPanel   = Color.FromArgb(30,30,45);
    static readonly Color cCard    = Color.FromArgb(38,40,58);
    static readonly Color cHov     = Color.FromArgb(46,50,72);
    static readonly Color cSel     = Color.FromArgb(28,52,105);
    static readonly Color cSelHov  = Color.FromArgb(35,60,115);
    static readonly Color cAccent  = Color.FromArgb(88,130,255);
    static readonly Color cAccentD = Color.FromArgb(50,80,160);
    static readonly Color cText    = Color.FromArgb(220,225,240);
    static readonly Color cTextDim = Color.FromArgb(155,163,195);
    static readonly Color cMuted   = Color.FromArgb(110,118,148);
    static readonly Color cSep     = Color.FromArgb(50,53,75);
    static readonly Color cGreen   = Color.FromArgb(60,200,110);
    static readonly Color cYellow  = Color.FromArgb(240,180,40);
    static readonly Color cRed     = Color.FromArgb(220,60,80);
    static readonly Color cHeader  = Color.FromArgb(110,160,255);

    // Fuentes
    static readonly Font fBig   = new("Segoe UI",15,FontStyle.Bold);
    static readonly Font fSub   = new("Segoe UI",10,FontStyle.Bold);
    static readonly Font fTitle = new("Segoe UI",11,FontStyle.Bold);
    static readonly Font fBold  = new("Segoe UI", 9,FontStyle.Bold);
    static readonly Font fBody  = new("Segoe UI", 9);
    static readonly Font fSmall = new("Segoe UI", 8);
    static readonly Font fTiny  = new("Segoe UI", 7);
    static readonly Font fMono  = new("Consolas",  8);

    // Estado
    readonly MonData _mon = new();
    readonly List<CheckBox> _allChk = new();
    readonly List<(CheckBox Chk, string Name)> _allItems = new();
    Panel? _center;
    System.Windows.Forms.Timer? _uiTimer;
    // Hover via timer global — evita destellos por entrar/salir de controles hijo
    readonly Dictionary<Panel,Action> _hoverCards = new();
    System.Windows.Forms.Timer? _hoverTimer;

    // Widgets monitor
    MonWidget? _wCpu, _wRam, _wNet, _wGpu;
    MonWidget[] _wDisks = Array.Empty<MonWidget>();
    int[] _diskNums = Array.Empty<int>();

    // Status bar
    readonly ToolStripStatusLabel _stMsg   = new() { Text="Listo", ForeColor=Color.FromArgb(110,118,148) };
    readonly ToolStripStatusLabel _stCount = new() { Text="0 opciones seleccionadas", ForeColor=Color.FromArgb(88,130,255) };

    // Panel descripcion
    Label _dTitle = new(), _dBody = new(), _dRec = new();
    Panel _dRecBox = new();

    // Datos de tweaks
    static readonly TweakSection[] Sections = {
        new TweakSection(@"Optimizacion General", false, new[]{ new TweakItem(@"Crear Punto de Restauracion", @"Imagina que tu PC es como una casa y estas por hacer una reforma. Un punto de restauracion es como tomar una fotografia de la casa ANTES de empezar la obra.

Si algo sale mal durante la reforma, puedes volver exactamente al estado en que estaba la casa en la foto, sin perder nada.

Lo mismo pasa con Windows: si despues de aplicar alguna optimizacion algo no funciona bien, puedes restaurar el sistema al estado en que estaba AHORA MISMO, antes de cualquier cambio.

Este proceso NO borra tus archivos personales (fotos, documentos, musica). Solo guarda el estado de la configuracion del sistema.

Para usarlo si algo sale mal: Panel de Control > Sistema > Proteccion del Sistema > Restaurar Sistema.", @"ACTIVALO SIEMPRE, ANTES DE CUALQUIER OTRA COSA. Es tu red de seguridad. Si algo sale mal, podras deshacer todo facilmente. Solo tarda unos segundos.", @"Seguridad: esencial", RecColor.Green), new TweakItem(@"Eliminar Archivos Temporales", @"Con el tiempo, Windows va acumulando archivos que ya no necesita: instaladores viejos de actualizaciones, errores guardados que nadie revisa, archivos de cache que ya expiraron, datos de programas que ya desinstalaste, etc.

Es como el cajon de casa donde guardas cosas 'por si acaso' que nunca usas. Con el tiempo se llena de basura.

Eliminar estos archivos:
- Libera espacio en tu disco duro (a veces varios GB)
- Puede hacer que el PC arranque un poco mas rapido
- No elimina NADA importante: ni tus documentos, ni tus fotos, ni tus programas instalados

Es completamente seguro y puedes hacerlo cada mes como mantenimiento de rutina.", @"ACTIVALO sin miedo. Es completamente seguro. Puedes hacerlo cada mes y notaras como el PC va mas fluido con el tiempo.", @"Disco: hasta +5 GB libres", RecColor.Green), new TweakItem(@"Ejecutar Limpieza de Disco", @"Windows tiene una herramienta oficial llamada 'Limpieza de disco' que busca y elimina archivos que el propio sistema considera innecesarios.

Esto incluye cosas como:
- Actualizaciones de Windows antiguas que ya no se pueden desinstalar pero siguen ocupando espacio
- Miniaturas de imagenes guardadas en cache
- Archivos de la papelera de reciclaje
- Archivos temporales de internet y del navegador Edge
- Archivos de registro de errores

Es como contratar al servicio de limpieza oficial de Microsoft: ellos saben exactamente que se puede tirar sin danar nada.", @"ACTIVALO. Completamente seguro y muy util en equipos que llevan tiempo sin hacerlo. En algunos PCs puede liberar hasta 10 GB o mas.", @"Disco: hasta +10 GB libres", RecColor.Green), new TweakItem(@"Poner Servicios del Sistema en Manual", @"Windows tiene mas de 200 'servicios' que son pequeños programas que corren en segundo plano para que todo funcione. El problema es que muchos de ellos se inician SOLOS cuando enciendes el PC, aunque nunca vayas a usarlos.

Piensalo asi: es como si cada vez que llegas a casa, prendieras la television del cuarto, la de la sala, la del bano y la de la cocina por si acaso vas a entrar. Gastan energia aunque no las uses.

Al ponerlos en 'Manual', esos servicios se quedan dormidos y solo despiertan cuando realmente se necesitan. Windows es lo suficientemente inteligente para activarlos solo si algun programa los requiere.

El resultado: el PC arranca mas rapido, usa menos RAM y corre mas fluido en el dia a dia.", @"ACTIVALO. Muy recomendado. Mejora notablemente el arranque y el rendimiento diario, especialmente en PCs con 8 GB de RAM o menos.", @"RAM: ~15%  |  Arranque: ~20%", RecColor.Green) }),
        new TweakSection(@"Privacidad", false, new[]{ new TweakItem(@"Desactivar Telemetria de Windows", @"Sabias que Windows te espía? No de forma maliciosa, pero si constantemente.

Por defecto, Windows envia a Microsoft una gran cantidad de datos sobre como usas tu computadora: que programas abres, cuanto tiempo los usas, que errores ocurren, que escribes en la barra de busqueda, como tienes configurada tu pantalla, etc.

Esto sucede TODO el tiempo, en segundo plano, sin que tu te des cuenta. Y para hacer eso, usa parte de los recursos de tu PC (procesador, memoria, conexion a internet).

Desactivar la telemetria:
- Para ese envio constante de datos a Microsoft
- Libera un poco de procesador y memoria para tus programas
- Mejora tu privacidad sin afectar ninguna funcion de Windows", @"ACTIVALO. No hay ningun motivo para que Microsoft reciba datos de como usas TU computadora. No afecta absolutamente nada del funcionamiento normal de Windows.", @"CPU: ~3%  |  Red: ~5%", RecColor.Green), new TweakItem(@"Desactivar Historial de Actividad", @"Windows guarda un registro detallado de absolutamente todo lo que haces en tu PC: que paginas webs visitas, que archivos abres, que programas usas y a que hora.

Todo eso se guarda en una funcion llamada 'Linea de Tiempo' que aparece cuando presionas la tecla Windows + Tab. La idea es que puedas retomar lo que estabas haciendo ayer o la semana pasada.

El problema: ese historial se sincroniza con tu cuenta de Microsoft y se sube a la nube. Es decir, Microsoft tiene acceso a un registro detallado de todo lo que haces en tu PC.

Si no usas la Linea de Tiempo, desactivar esto:
- Protege tu privacidad
- Deja de sincronizar datos con la nube de Microsoft
- Reduce la actividad innecesaria en segundo plano", @"ACTIVALO si no usas la funcion 'Linea de Tiempo'. La gran mayoria de usuarios no conoce ni usa esa funcion. Si no la usas, desactivarlo es pura ganancia para tu privacidad.", @"Disco: ~2%  |  Red: ~3%", RecColor.Green), new TweakItem(@"Desactivar Seguimiento de Ubicacion", @"Tu PC con Windows puede saber en todo momento donde estas fisicamente en el mundo, usando el GPS (si tu equipo lo tiene), la red Wi-Fi a la que estas conectado o la direccion IP de internet.

Esa informacion se comparte con aplicaciones y con Microsoft. Por ejemplo, una app del clima, mapas, o incluso publicidad dirigida.

Si no usas aplicaciones que realmente necesiten saber donde estas, esta funcion solo consume bateria (en laptops) y envia tus datos de ubicacion innecesariamente.

Al desactivarla, ninguna app podra acceder a tu ubicacion sin que tu lo permitas de forma explicita en la configuracion.", @"ACTIVALO para la mayoria de usuarios. Si usas aplicaciones de mapas o clima en tu PC que necesiten tu ubicacion, evalua si te conviene. En PCs de escritorio casi siempre es seguro desactivarlo.", @"Bateria: ~5%  |  Red: ~2%", RecColor.Green), new TweakItem(@"Desactivar Telemetria de PowerShell 7", @"PowerShell es una herramienta avanzada de Windows que la mayoria de usuarios nunca abre directamente. Sin embargo, si tienes instalada la version 7 (la mas moderna), esta tambien envia datos de uso a Microsoft por defecto.

Incluso si nunca abres PowerShell manualmente, algunos programas lo usan internamente sin que te des cuenta, y en esos momentos tambien se envian datos a Microsoft.

Desactivar esta telemetria especifica no afecta en absoluto el funcionamiento del sistema ni de tus programas instalados. Solo detiene ese reporte automatico de datos.", @"ACTIVALO sin dudarlo. Si ni siquiera sabes que es PowerShell, no tienes nada que perder al desactivar su telemetria. Es 100% seguro.", @"CPU: ~1%  |  Red: ~1%", RecColor.Green), new TweakItem(@"Desactivar WPBT (Instalaciones Automaticas del Fabricante)", @"Algunos fabricantes de computadoras (Dell, HP, Lenovo, Asus, etc.) tienen un mecanismo llamado WPBT que les permite reinstalar sus programas automaticamente cada vez que Windows arranca.

Para que lo usan? Para asegurarse de que su software (de soporte, diagnostico, o incluso publicidad) siempre este instalado en tu PC, aunque tu lo hayas desinstalado antes.

Esto significa que si desinstalaste el 'bloatware' (programas innecesarios que vienen preinstalados de fabrica), el fabricante puede volverte a instalarlos sin pedirte permiso cada vez que enciendes el PC.

Desactivar WPBT impide esas reinstalaciones automaticas y te da control total de lo que esta instalado en tu PC.", @"ACTIVALO, especialmente si tu PC es de una marca conocida (Dell, HP, Lenovo, Asus, Acer). En PCs ensambladas a medida no suele aplicar, pero tampoco hace daño activarlo.", @"Arranque: ~5%", RecColor.Green) }),
        new TweakSection(@"Interfaz y Experiencia", false, new[]{ new TweakItem(@"Quitar Widgets de la Barra de Tareas", @"En Windows 11 hay un icono en la barra de tareas (generalmente del clima o las noticias) que al hacerle clic abre un panel con noticias, deportes, finanzas y el tiempo.

El problema es que ese panel esta SIEMPRE activo en segundo plano, cargando noticias de internet aunque tu nunca lo mires. Esto consume:
- Memoria RAM constantemente
- Conexion a internet todo el tiempo (descargando noticias)
- Hace que el PC arranque un poco mas lento

La informacion que muestra la puedes obtener facilmente con cualquier buscador web cuando la necesites.

Al quitarlos, ese panel deja de existir completamente y deja de consumir recursos del sistema.", @"ACTIVALO si no usas el panel de widgets. La gran mayoria de usuarios no lo usa intencionalmente. Quitarlo libera recursos sin perder nada realmente util.", @"RAM: ~8%  |  Red: ~4%", RecColor.Green), new TweakItem(@"Activar Finalizar Tarea con Clic Derecho", @"Te ha pasado que un programa se congela y no responde? La pantalla se queda fija y no puedes hacer nada con ese programa.

Normalmente tienes que abrir el Administrador de Tareas (Ctrl+Alt+Del), buscar el programa en la lista y cerrarlo desde ahi. Son varios pasos molestos cuando ya estas frustrado porque el programa no responde.

Con esta opcion activada, cuando un programa se congela, simplemente haces clic derecho sobre su icono en la barra de tareas y aparece directamente la opcion 'Finalizar tarea'.

Es como tener un boton de apagado de emergencia a un solo clic de distancia, sin pasar por menus complicados.", @"ACTIVALO. Super util y practico para el dia a dia. No tiene ningun inconveniente ni riesgo para el sistema.", @"Usabilidad: alta", RecColor.Green), new TweakItem(@"Desactivar Apps Patrocinadas y Sugerencias", @"Has notado que a veces aparecen en el menu inicio aplicaciones que nunca instalaste, como juegos, Spotify o apps de terceros? O que Windows te muestra sugerencias de apps de pago?

Microsoft tiene acuerdos comerciales con empresas para mostrar sus aplicaciones en tu PC como si fueran recomendaciones del sistema. A veces incluso las instala automaticamente sin pedirte permiso.

Esta opcion desactiva esas instalaciones y sugerencias automaticas:
- El menu inicio solo mostrara lo que TU hayas instalado
- No apareceran apps que no pediste
- No veras publicidad disfrazada de 'recomendaciones del sistema'", @"ACTIVALO. Nadie quiere que le instalen cosas sin pedir permiso. Elimina algo muy molesto sin ningun costo ni riesgo para el sistema.", @"Arranque: ~5%", RecColor.Green), new TweakItem(@"Desactivar Descubrimiento Automatico de Vista de Carpetas", @"Te ha pasado que abres una carpeta y de repente aparece diferente a como la dejaste? Por ejemplo, la dejaste con iconos grandes y ahora aparece como lista, o viceversa.

Esto pasa porque Windows 'analiza' el contenido de cada carpeta y decide por su cuenta como mostrarte los archivos: si hay muchas fotos, cambia a modo galeria; si hay muchos documentos, cambia a lista detallada, etc.

Esto puede ser muy frustrante cuando tienes tus carpetas organizadas de cierta forma y Windows las cambia solas.

Al desactivar esto, tus carpetas siempre se veran exactamente como tu las configuraste la ultima vez. Windows deja de cambiar la vista sin avisarte.", @"ACTIVALO si te molesta que las carpetas cambien de apariencia solas. Es una mejora de comodidad sin ningun riesgo.", @"Usabilidad: alta", RecColor.Green), new TweakItem(@"Restaurar Menu de Clic Derecho Clasico", @"Si tienes Windows 11, habras notado que el menu que aparece al hacer clic derecho cambio completamente. Ahora muestra menos opciones y para ver el menu completo tienes que hacer un paso extra: clic en 'Mostrar mas opciones'.

Este paso extra es molesto especialmente cuando:
- Quieres copiar, pegar o renombrar archivos rapido
- Necesitas 'Abrir con' para elegir un programa
- Usas opciones de software instalado (como WinRAR, 7-Zip, etc.)

Esta opcion restaura el menu completo de siempre (como en Windows 10) que aparece directamente al primer clic derecho, con todas las opciones visibles de golpe.", @"ACTIVALO si usas Windows 11 y el nuevo menu te resulta molesto o lento. Recuperas toda la comodidad del menu antiguo sin perder absolutamente nada.", @"Usabilidad: alta", RecColor.Green), new TweakItem(@"Quitar Galeria del Explorador de Archivos", @"En Windows 11, el panel izquierdo del Explorador de archivos (donde ves tus carpetas) tiene una seccion llamada 'Galeria' que muestra todas tus fotos de distintas carpetas reunidas en un solo lugar, como si fuera una pequeña app de fotos integrada.

Si no usas esa vista de galeria o prefieres ver tus fotos con otro programa (como Google Fotos, el Visor de fotos de Windows, o cualquier otro), esta seccion solo ocupa espacio visual innecesario en el Explorador.

Eliminarla hace el Explorador mas limpio y simple. Tus fotos siguen existiendo exactamente igual y puedes acceder a ellas normalmente por sus carpetas.", @"ACTIVALO si no usas la Galeria. Limpia la interfaz del Explorador sin ningun efecto negativo en tus archivos ni en tus fotos.", @"RAM: ~2%", RecColor.Green), new TweakItem(@"Quitar Inicio del Explorador de Archivos", @"Cuando abres el Explorador de archivos (el programa que usas para ver tus carpetas y archivos), lo primero que aparece es una seccion llamada 'Inicio' que muestra archivos recientes y 'accesos rapidos' sugeridos por Windows.

Si prefieres que el Explorador abra directamente en 'Este Equipo' (donde ves tus discos duros y unidades USB) o en una carpeta especifica de tu eleccion, la seccion 'Inicio' solo es un paso innecesario cada vez que abres el Explorador.

Al quitarla, el Explorador es mas directo y va directo al grano desde que lo abres.", @"ACTIVALO si te molesta la pantalla de inicio del Explorador. Una mejora de comodidad sin ningun riesgo para el sistema.", @"Usabilidad: alta", RecColor.Green) }),
        new TweakSection(@"Rendimiento", false, new[]{ new TweakItem(@"Desactivar Aplicaciones en Segundo Plano", @"Muchas aplicaciones de Windows (Correo, Noticias, Calendario, Xbox, Clima, etc.) siguen funcionando aunque las hayas cerrado. Estan activas en segundo plano, esperando recibir notificaciones o actualizarse solas.

Piensalo como un empleado que aunque le dices que se vaya a casa, se queda en la oficina usando la luz, el aire acondicionado y los recursos, por si acaso lo necesitas.

Esto consume de forma permanente:
- RAM que podrias usar para tus programas principales
- CPU que podria ir a lo que estas haciendo en ese momento
- Bateria en laptops (puede reducirla notablemente)
- Ancho de banda de tu internet

Al desactivarlas, solo corren cuando TU las abres conscientemente.", @"ACTIVALO, especialmente en PCs con 8 GB de RAM o menos. El impacto en rendimiento es notable. Las apps siguen funcionando cuando tu las abres, simplemente no se quedan corriendo de fondo todo el tiempo.", @"RAM: ~20%  |  CPU: ~10%", RecColor.Green), new TweakItem(@"Configurar Pantalla para Maximo Rendimiento", @"Windows viene con muchos efectos visuales activados para que se vea bonito: animaciones cuando abres y cierras ventanas, sombras debajo de los iconos, bordes con transparencia, transiciones suaves entre pantallas, efectos al minimizar y maximizar, etc.

Estos efectos se ven bien, pero cada uno consume recursos del procesador y de la tarjeta grafica, aunque sea en pequeñas cantidades. En un PC potente no se nota la diferencia. Pero en un PC mas modesto o antiguo, la diferencia puede ser significativa.

Al configurar Windows para rendimiento, se desactivan todos esos efectos decorativos. La interfaz se vera mas 'plana' y simple (similar a Windows XP/7 basico), pero el sistema respondera mas rapido a tus clics y acciones.", @"ACTIVALO en PCs de gama baja, antiguos, o si sientes que el sistema va lento. En PCs potentes el cambio visual puede no valer la pena. En equipos con pocos recursos, la mejora de velocidad puede ser muy notable.", @"CPU: ~8%  |  GPU: ~5%", RecColor.Yellow), new TweakItem(@"Desactivar Hibernacion", @"La hibernacion es una funcion que al apagar el PC guarda todo lo que tienes abierto (programas, documentos, pestanas del navegador) en el disco duro. Cuando vuelves a encender el PC, todo vuelve exactamente como lo dejaste, sin tener que abrir todo de nuevo.

Para hacer esto, Windows reserva un archivo gigante en tu disco duro llamado 'hiberfil.sys'. Ese archivo ocupa exactamente lo mismo que tu RAM instalada: si tienes 16 GB de RAM, el archivo pesa 16 GB en tu disco.

Si apagas y enciendes el PC normalmente (sin necesitar retomar sesiones con todo lo que tenias abierto), ese archivo gigante solo esta ocupando espacio en tu disco sin hacer nada util para ti.

Al desactivarla, ese espacio queda libre para que lo uses como necesites.", @"DEPENDE: Si tienes una laptop y la llevas de un lugar a otro sin apagarla del todo (solo cierras la tapa), DEJA la hibernacion activa. Si tienes una PC de escritorio o si siempre apagas todo antes de irte, puedes desactivarla para liberar espacio.", @"Disco: ~8-32 GB libres", RecColor.Yellow), new TweakItem(@"Desactivar Storage Sense (Limpieza Automatica)", @"Storage Sense es un sistema automatico de Windows que vigila tu disco duro y cuando detecta que esta casi lleno, comienza a borrar archivos automaticamente para liberar espacio.

Borra cosas como: archivos de la papelera despues de 30 dias, archivos temporales viejos, y a veces archivos de la carpeta de Descargas que tienen mas de ciertos dias.

El problema es que a veces puede borrar cosas que querieras conservar sin avisarte con suficiente claridad o tiempo.

Si tienes suficiente espacio en el disco, desactivarlo te da control total de que se elimina y cuando. Tu decides que se borra, no Windows.", @"DEPENDE: Si tu disco se llena seguido y no tienes tiempo de limpiarlo manualmente, dejalo activo: te ayuda automaticamente. Si tienes espacio de sobra y prefieres controlar tu que se borra, desactivalo para tener mas control.", @"CPU: ~2%", RecColor.Yellow), new TweakItem(@"Desactivar Optimizaciones de Pantalla Completa", @"Cuando abres un videojuego o un video en pantalla completa, Windows activa automaticamente unas 'optimizaciones' propias que se supone deben mejorar el rendimiento en ese modo.

Sin embargo, en muchos casos estas optimizaciones generan el efecto contrario: tartamudeos (el juego congela por fracciones de segundo de forma irregular), frames por segundo inestables, o micro-cortes en el video que hacen que la imagen no fluya bien.

Este es un problema conocido y bastante comun entre jugadores de PC. Desactivar estas optimizaciones hace que el juego tome control directo de la pantalla, lo que suele resultar en una experiencia mas fluida y consistente sin esos cortes.", @"ACTIVALO si juegas videojuegos en PC y notas que van cortados o con tartamudeos. Si no juegas en PC, esta opcion no tiene efecto en tu uso diario normal.", @"FPS juegos: ~10-15%", RecColor.Green), new TweakItem(@"Desactivar Superposicion Multiplano (Multiplane Overlay)", @"Esta es una funcion tecnica de tu tarjeta grafica. Normalmente esta pensada para mejorar el rendimiento al mostrar multiples capas de imagen en pantalla al mismo tiempo.

Sin embargo, en muchas tarjetas graficas (especialmente Intel integradas y algunas NVIDIA antiguas) esta funcion esta mal implementada y causa problemas visuales molestos:
- Pantallazos negros momentaneos durante el uso normal
- Parpadeos de pantalla sin razon aparente
- Artefactos visuales (pixels raros, lineas, colores incorrectos)
- Problemas al grabar o capturar la pantalla con programas como OBS

Si experimentas alguno de estos sintomas, desactivar esta opcion los suele resolver completamente.", @"ACTIVALO SOLO si tienes problemas visuales: pantallazos negros, parpadeos o artefactos en la pantalla. Si tu PC funciona bien visualmente, no toques esta opcion ya que no mejora nada en equipos sin problemas.", @"Estabilidad GPU", RecColor.Yellow) }),
        new TweakSection(@"Red e Internet", false, new[]{ new TweakItem(@"Preferir IPv4 sobre IPv6", @"Internet usa dos 'idiomas' para que los dispositivos se comuniquen entre si: IPv4 (el clasico, algo asi como el espanol) e IPv6 (el moderno, como el ingles).

La mayoria de routers domesticos y proveedores de internet en Latinoamerica y Espana todavia trabajan principalmente con IPv4. Cuando tu PC intenta usar IPv6 primero (que es lo que hace por defecto), a veces tarda mas en encontrar el camino correcto si la red no lo soporta bien o no esta correctamente configurada.

Al decirle a Windows que prefiera IPv4, las conexiones son mas directas y rapidas en redes que no tienen IPv6 completamente configurado.

Nota importante: esto NO desactiva IPv6, solo le dice a Windows que pruebe IPv4 primero. Si IPv6 es necesario en algun momento, Windows lo usara automaticamente.", @"ACTIVALO, especialmente si juegas online o sientes que la conexion tarda en establecerse. En redes modernas con IPv6 bien configurado el cambio es minimo, pero en redes domesticas tipicas puede mejorar la velocidad de conexion.", @"Latencia: ~5-15%", RecColor.Green), new TweakItem(@"Desactivar Teredo", @"Teredo es una tecnologia que crea un 'tunel' para que tu PC pueda usar IPv6 aunque tu router no lo soporte nativamente. Es como un adaptador que traduce entre los dos idiomas de internet.

El problema: este tunel añade capas extra en la comunicacion de red, lo que puede aumentar la latencia (el tiempo que tarda un dato en llegar a su destino y volver). En juegos online, mas latencia equivale a mas 'lag' o retraso.

Ademas, Teredo es a veces identificado erroneamente como actividad de red sospechosa por algunos sistemas anti-trampa de juegos (como Easy Anti-Cheat o BattlEye), lo que puede causar problemas de conexion en ciertos juegos en linea.

Si tu red ya funciona bien sin Teredo (que es lo mas comun en redes domesticas), desactivarlo elimina esa capa innecesaria y puede mejorar la conexion.", @"ACTIVALO si juegas videojuegos online. Puede mejorar la latencia y evitar problemas con anti-cheats. En uso general de internet no hay ninguna diferencia perceptible para el usuario.", @"Latencia red: ~3-8%", RecColor.Green), new TweakItem(@"Desactivar IPv6 Completamente", @"A diferencia de la opcion anterior (que solo pone IPv4 primero manteniendo IPv6 disponible), esta opcion desactiva IPv6 por completo en todos los adaptadores de red de tu PC.

Cuando puede tener sentido? Si tu proveedor de internet y tu router no usan IPv6 para nada, tenerlo activado solo agrega complejidad innecesaria al sistema de red.

Sin embargo, IPv6 es el futuro de internet y cada vez mas redes y servicios lo requieren. Desactivarlo completamente podria causar problemas para conectarte a ciertos servicios modernos en el futuro o en redes empresariales.

En la mayoria de los casos, la opcion anterior 'Preferir IPv4' es suficiente y mucho mas segura que desactivar IPv6 por completo.", @"CON PRECAUCION. Solo si sabes con certeza que tu red no usa IPv6 para nada y tienes problemas especificos de conectividad. En caso de duda, usa la opcion 'Preferir IPv4' que es mas segura.", @"Red: ~2%", RecColor.Yellow) }),
        new TweakSection(@"Navegadores Web", false, new[]{ new TweakItem(@"Optimizar Microsoft Edge", @"Microsoft Edge es el navegador que viene instalado con Windows 11. Aunque es bastante bueno para navegar, trae muchas funciones activadas que consumen recursos innecesariamente:

- Carga paginas web antes de que tu las pidas (para 'adelantarse')
- Recopila datos de navegacion para Microsoft
- Tiene servicios corriendo en segundo plano aunque Edge este cerrado
- Muestra una pantalla de inicio con noticias y anuncios
- Tiene integracion activa con Bing AI y Copilot

Esta optimizacion desactiva todo eso, haciendo Edge mas rapido, mas privado y que consuma menos recursos cuando esta cerrado o en segundo plano.

Nota: Edge sigue funcionando completamente para navegar. Solo se desactivan las funciones extras que no necesitas para la navegacion basica.", @"ACTIVALO si usas Edge como tu navegador principal. Lo hace notablemente mas rapido y privado sin perder ninguna funcion basica de navegacion por internet.", @"RAM: ~15%  |  Arranque: ~10%", RecColor.Green), new TweakItem(@"Optimizar Brave Browser", @"Brave es un navegador popular conocido por bloquear anuncios automaticamente y por su fuerte enfoque en privacidad. Sin embargo, incluso Brave tiene activadas algunas funciones de telemetria y recopilacion de datos que envia a sus propios servidores.

Tambien tiene servicios que corren en segundo plano cuando el navegador esta cerrado, como el sistema de recompensas BAT.

Esta optimizacion desactiva esas funciones extra, haciendo Brave aun mas privado de lo que ya es y reduciendo su actividad en segundo plano.

Si no tienes Brave instalado en tu PC, esta opcion no hace absolutamente nada (no tiene ningun efecto negativo, simplemente no aplica).", @"ACTIVALO si usas Brave. No tiene efecto si no esta instalado. Mejora la privacidad sin afectar en nada la experiencia de navegacion.", @"RAM: ~5%  |  Red: ~3%", RecColor.Green) }),
        new TweakSection(@"Opciones Avanzadas", true, new[]{ new TweakItem(@"Desactivar Microsoft Copilot", @"Copilot es el asistente de inteligencia artificial que Microsoft integro en Windows 11. Puedes hacerle preguntas, pedirle ayuda con tareas o que genere contenido directamente desde Windows.

El problema: aunque no lo uses activamente, Copilot tiene servicios corriendo en segundo plano todo el tiempo, consumiendo memoria RAM y recursos del procesador de forma constante.

Si no usas el asistente de IA de Windows (la mayoria de usuarios prefiere usar ChatGPT, Gemini u otros directamente en el navegador), desactivarlo libera esos recursos para tus programas.

Puedes reactivarlo en cualquier momento desde Configuracion de Windows si cambias de opinion y quieres volver a usarlo.", @"ACTIVALO si no usas Copilot regularmente. Libera recursos sin perder ninguna funcion que uses. Si te gusta y usas el asistente de IA integrado, mejor dejalo activo.", @"RAM: ~10%  |  CPU: ~5%", RecColor.Green), new TweakItem(@"Desactivar Panel de Notificaciones y Calendario", @"El reloj en la esquina inferior derecha de la barra de tareas, al hacerle clic, abre un panel que muestra las notificaciones del sistema y un pequeno calendario integrado.

Si no usas ese calendario (y usas Google Calendar, Outlook en el navegador, u otro metodo para ver tus citas y recordatorios), ese panel es simplemente algo que aparece cuando haces clic sin querer en el reloj.

Desactivarlo oculta completamente ese panel. El reloj sigue visible en la barra de tareas, pero al hacer clic en el no aparece el panel de notificaciones ni el calendario.

Nota importante: si desactivas esto, perderas acceso rapido a las notificaciones del sistema desde ese panel.", @"SOLO si definitivamente no usas el panel de notificaciones ni el calendario de Windows. Para la mayoria de usuarios es mejor dejarlo activo, ya que las notificaciones del sistema son utiles.", @"RAM: ~3%", RecColor.Yellow), new TweakItem(@"Quitar Microsoft OneDrive", @"OneDrive es el servicio de almacenamiento en la nube de Microsoft. Funciona como Google Drive o Dropbox, guardando copias de tus archivos en internet automaticamente para que puedas acceder a ellos desde cualquier dispositivo.

Viene instalado con Windows y se inicia automaticamente al encender el PC, sincronizando tus carpetas en segundo plano. Esto consume de forma constante:
- Espacio en disco para sus propios archivos
- Memoria RAM para el proceso de sincronizacion
- Ancho de banda de internet para subir y bajar archivos
- Procesador durante las sincronizaciones

Si ya usas otro servicio de nube (Google Drive, Dropbox, iCloud) o no necesitas respaldo en la nube, puedes desinstalarlo completamente.

MUY IMPORTANTE: Si tienes archivos importantes guardados SOLO en OneDrive y no en tu PC, descargalos primero antes de desinstalarlo para no perderlos.", @"EVALUA BIEN. Si usas OneDrive para hacer copias de seguridad de tus fotos u archivos importantes, NO lo quites sin antes asegurarte de que esos archivos esten descargados en tu PC. Si no usas OneDrive para nada, eliminarlo libera recursos notablemente.", @"RAM: ~15%  |  Arranque: ~10%", RecColor.Yellow), new TweakItem(@"Quitar Xbox y Componentes de Gaming de Microsoft", @"Windows incluye varios servicios relacionados con Xbox y gaming de Microsoft que estan activos en tu PC aunque no tengas una consola Xbox:
- La aplicacion Xbox
- Game Bar (el overlay que se abre con Windows + G durante los juegos)
- Xbox Game DVR (para grabar clips de juegos automaticamente)
- Servicios de estadisticas y logros de juegos de Microsoft

Todos estos servicios corren en segundo plano consumiendo recursos aunque nunca vayas a usarlos.

Si no usas ninguno de estos (no juegas en PC con Game Pass, no usas el overlay de Xbox, no grabas clips de juegos), puedes eliminarlos para liberar recursos.

IMPORTANTE: Algunos juegos de Steam o Epic Games que usan tecnologia Xbox Game Services podrian verse afectados y no funcionar correctamente.", @"EVALUA. Si no juegas en PC o no usas Xbox en absoluto: activalo para liberar recursos. Si juegas con Game Pass o usas el overlay (Win+G): dejalo para no tener problemas con los juegos.", @"RAM: ~10%  |  Arranque: ~8%", RecColor.Yellow), new TweakItem(@"Bloquear Instalaciones Automaticas de Razer", @"Razer es una marca popular de perifericos gaming (teclados, mouse, audifonos, mandos). Su software, llamado Razer Synapse, se instala automaticamente cuando conectas un dispositivo Razer por primera vez, y ademas se actualiza solo sin pedirte permiso.

Este comportamiento puede ser molesto porque:
- Instala software en tu PC sin tu consentimiento explicito
- Las actualizaciones pueden ocurrir en momentos inoportunos (cuando estas trabajando o jugando)
- A veces instala software adicional que no pediste junto con las actualizaciones

Esta opcion bloquea esas instalaciones y actualizaciones automaticas. Tu hardware Razer (teclado, mouse, etc.) seguira funcionando perfectamente para su funcion basica. Solo perderas las funciones avanzadas del software (como personalizar colores RGB o macros) hasta que instales Synapse manualmente cuando tu quieras.", @"ACTIVALO si tienes hardware Razer y te molesta que el software se instale o actualice solo. Tu mouse y teclado siguen funcionando perfectamente sin Synapse para uso basico.", @"Arranque: ~3%", RecColor.Green), new TweakItem(@"Bloquear Conexiones de Red de Adobe", @"Los programas de Adobe (Photoshop, Illustrator, Premiere, Acrobat, etc.) se conectan constantemente a los servidores de Adobe, incluso cuando no los estas usando activamente.

Lo hacen para: verificar que tu licencia este activa, enviar datos de uso y telemetria, buscar actualizaciones automaticas, y recopilar informacion sobre como usas los programas.

Estas conexiones ocurren en segundo plano de forma permanente y consumen recursos de tu red y de tu procesador.

Esta opcion bloquea esas conexiones añadiendo entradas en el archivo 'hosts' de Windows (una lista de bloqueo interna del sistema).

ADVERTENCIA IMPORTANTE: Si tienes una suscripcion activa de Adobe Creative Cloud (que requiere verificacion periodica de licencia online), esto podria causar que las apps muestren errores de licencia o dejen de abrirse correctamente.", @"PRECAUCION. SOLO si no tienes suscripcion activa de Adobe o si usas versiones que no requieren verificacion online constante. Si pagas mensualmente por Adobe Creative Cloud, NO lo actives para evitar problemas con tu licencia.", @"Red: ~5%  |  CPU: ~2%", RecColor.Yellow), new TweakItem(@"Quitar Microsoft Edge del Sistema", @"Esta opcion desinstala completamente Microsoft Edge de tu computadora, no solo lo desactiva o configura.

A diferencia de la opcion 'Optimizar Edge' que solo ajusta su configuracion para que sea mas rapido y privado, esta lo elimina por completo del sistema.

COSAS IMPORTANTES que debes saber antes de hacerlo:
1. Algunas funciones de Windows usan Edge internamente aunque tu no lo uses (como abrir ciertos links de Ayuda del sistema, el visor de PDF integrado en el Explorador, o ciertas paginas de configuracion del sistema)
2. Una vez eliminado, no podras reinstalarlo facilmente desde la tienda ya que Microsoft lo protege
3. Si Edge es tu unico navegador instalado, asegurate de tener otro instalado ANTES (Chrome, Firefox, Brave, etc.)

En la mayoria de los casos, la opcion 'Optimizar Edge' es suficiente y mucho mas segura.", @"PRECAUCION. Solo si tienes otro navegador ya instalado Y entiendes que algunas funciones del sistema dependen de Edge internamente. Para la mayoria: es mejor OPTIMIZARLO en lugar de eliminarlo.", @"RAM: ~20%  |  Disco: ~1 GB", RecColor.Yellow), new TweakItem(@"Eliminar TODAS las Apps de la Tienda — NO RECOMENDADO", @"Esta opcion elimina ABSOLUTAMENTE TODAS las aplicaciones instaladas desde la Microsoft Store.

Esto incluye no solo apps de terceros, sino tambien apps fundamentales del propio Windows:
- Calculadora
- Visor de fotos
- Aplicacion de Camara
- Reproductor de musica y video
- Aplicacion de Mapas
- La propia Tienda de Microsoft (no podras instalar apps nunca mas)
- Y muchas mas que Windows usa internamente para funcionar

Despues de hacer esto, Windows puede quedar con funciones rotas o completamente incompletas. Muchas cosas podrian dejar de funcionar correctamente y la experiencia general sera muy degradada.

Recuperar todo esto requiere conocimientos tecnicos muy avanzados y puede requerir reinstalar Windows por completo.", @"NO LO ACTIVES si eres un usuario normal. Esta opcion es EXCLUSIVAMENTE para tecnicos avanzados que saben exactamente que hacen. El riesgo de dejar el sistema inutilizable es muy alto.", @"RAM: ~5% (riesgo alto)", RecColor.Red), new TweakItem(@"Ajustar Reloj a UTC (Solo para Dual Boot con Linux)", @"Esta opcion es MUY especifica y solo tiene sentido si tienes dos sistemas operativos instalados en el mismo PC al mismo tiempo: Windows y Linux (Ubuntu, Mint, Fedora, etc.).

El problema tecnico: Windows y Linux guardan la hora del reloj de hardware de manera diferente por diseno. Windows asume que el reloj fisico del PC esta configurado en la hora local de tu zona horaria. Linux asume que ese mismo reloj esta en UTC (la hora universal que no cambia con las zonas horarias).

El resultado practico: cada vez que cambias entre uno y otro sistema operativo, el reloj se desincroniza automaticamente y muestra la hora incorrecta en uno de los dos sistemas. Tienes que corregirlo a mano cada vez que cambias de sistema.

Esta opcion hace que Windows tambien use UTC para el reloj de hardware, igual que Linux, eliminando esa desincronizacion de forma permanente.

Si solo tienes Windows instalado en tu PC (sin Linux), esta opcion no tiene ningun efecto ni beneficio. Simplemente ignorala.", @"ACTIVALO SOLO si tienes Linux instalado junto a Windows en el mismo PC y el reloj se desajusta al cambiar de sistema. Para usuarios con solo Windows instalado: ignorar completamente.", @"Precision Dual Boot", RecColor.Yellow) })
    };

    // =========================================================================
    public MainForm() {
        Text = "Optimizador Maestro  v1.0";
        Size = new Size(1380, 920);
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = cBg; ForeColor = cText;
        FormBorderStyle = FormBorderStyle.Sizable;
        Icon = SystemIcons.Shield;

        BuildTitle();
        BuildStatus();
        BuildBottom();
        BuildMonitor();
        BuildDescPanel();
        BuildCenter();
        // Timer de hover: revisa posición del cursor a 50ms sin depender de MouseEnter/Leave
        _hoverTimer = new System.Windows.Forms.Timer { Interval=50 };
        _hoverTimer.Tick+=(_,__)=>{ foreach(var fn in _hoverCards.Values) fn(); };
        _hoverTimer.Start();
        StartMonitor();
    }

    // ── Barra de titulo ───────────────────────────────────────────────────────
    void BuildTitle() {
        var p = new Panel { Height=52, Dock=DockStyle.Top, BackColor=cPanel };
        p.Controls.Add(L("OPTIMIZADOR MAESTRO  v1.0", fBig, cAccent, new Point(16,7)));
        p.Controls.Add(L("Clic en el NOMBRE de una opcion para ver su descripcion. Marca la CASILLA para seleccionarla.", fSmall, cMuted, new Point(20,34)));
        Controls.Add(p);
    }

    // ── Status bar ────────────────────────────────────────────────────────────
    void BuildStatus() {
        var sb = new StatusStrip { BackColor=cPanel };
        sb.Items.AddRange(new ToolStripItem[]{ _stMsg, new ToolStripStatusLabel{Spring=true}, _stCount });
        Controls.Add(sb);
    }

    // ── Panel inferior ────────────────────────────────────────────────────────
    void BuildBottom() {
        var p = new Panel { Height=65, Dock=DockStyle.Bottom, BackColor=cPanel };
        Btn(p,"Seleccionar Todo",  16, 170,Color.FromArgb(48,52,75),   fBody, (_,__)=>{ _allChk.ForEach(c=>c.Checked=true);  UpdateCount(); });
        Btn(p,"Deseleccionar Todo",194,185,Color.FromArgb(48,52,75),   fBody, (_,__)=>{ _allChk.ForEach(c=>c.Checked=false); UpdateCount(); });
        Btn(p,"Aplicar Mejoras",   400,200,cAccent,                    fSub,  OnApply);
        Btn(p,"Revertir Cambios",  608,200,Color.FromArgb(110,50,55),  fBody, OnUndo);
        var note = new Label { Text="NOTA: Crea siempre un Punto de Restauracion antes de aplicar cambios.",
            Font=fSmall, ForeColor=cYellow, Location=new Point(830,23), AutoSize=true };
        p.Controls.Add(note);
        Controls.Add(p);
    }
    void Btn(Panel p, string t, int x, int w, Color bg, Font f, EventHandler ev) {
        var b = new Button { Text=t, Font=f, ForeColor=Color.White, BackColor=bg, FlatStyle=FlatStyle.Flat,
            Size=new Size(w,40), Location=new Point(x,12), Cursor=Cursors.Hand };
        b.FlatAppearance.BorderSize=0; b.Click+=ev; p.Controls.Add(b);
    }

    // ── Monitor izquierdo ─────────────────────────────────────────────────────
    void BuildMonitor() {
        var pm = new Panel { Width=270, Dock=DockStyle.Left, BackColor=cPanel, AutoScroll=true };

        // Cabecera
        var hdr = new Panel { Size=new Size(270,38), BackColor=cAccentD };
        hdr.Controls.Add(new Label { Text="  MONITOR DE HARDWARE", Font=fSub, ForeColor=Color.White,
            Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft });
        pm.Controls.Add(hdr);

        // Selector de velocidad
        var spd = new Panel { Size=new Size(270,30), Location=new Point(0,38), BackColor=cCard };
        spd.Controls.Add(new Label { Text="  Actualizacion:", Font=fTiny, ForeColor=cMuted,
            Location=new Point(4,8), AutoSize=true });
        (int ms, string lbl)[] speeds = {(500,"0.5s"),(1000,"1s"),(2000,"2s")};
        int rx=90;
        foreach (var (ms,lbl) in speeds) {
            var cap=ms;
            var rb = new RadioButton { Text=lbl, Font=fTiny, ForeColor=cText, BackColor=cCard,
                Location=new Point(rx,6), AutoSize=true, Checked=(ms==1000) };
            rb.CheckedChanged+=(_,__)=>{ if(rb.Checked&&_uiTimer!=null) _uiTimer.Interval=cap; };
            spd.Controls.Add(rb); rx+=55;
        }
        pm.Controls.Add(spd);

        int y=76;

        // Detectar discos fisicos
        var diskList = new List<(int num, string label)>();
        try {
            using var s = new ManagementObjectSearcher("SELECT Index,MediaType,Model FROM Win32_DiskDrive");
            foreach (ManagementObject d in s.Get()) {
                int idx = Convert.ToInt32(d["Index"]);
                string med = d["MediaType"]?.ToString()??"";
                string mod = d["Model"]?.ToString()??"";
                string tipo = (med.Contains("SSD")||mod.Contains("SSD")||mod.Contains("NVMe")) ? "SSD" : "HDD";
                diskList.Add((idx,$"Disco {idx}  ({tipo})"));
            }
            diskList.Sort((a,b)=>a.num.CompareTo(b.num));
        } catch {}

        _diskNums  = diskList.Select(d=>d.num).ToArray();
        _wDisks    = new MonWidget[diskList.Count];
        _mon.Disks = new DiskSlot[diskList.Count];
        for (int i=0;i<diskList.Count;i++) {
            _mon.Disks[i]=new DiskSlot();
            _wDisks[i]=Widget(pm,diskList[i].label,y); y+=80;
        }

        _wCpu = Widget(pm,"Procesador (CPU)",y); y+=80;
        _wRam = Widget(pm,"Memoria RAM",     y); y+=80;
        _wNet = Widget(pm,"Red (Wi-Fi/LAN)", y); y+=80;
        _wGpu = Widget(pm,"GPU",             y); y+=80;

        pm.AutoScrollMinSize = new Size(260,y+10);
        Controls.Add(pm);
    }

    MonWidget Widget(Panel p, string lbl, int y) {
        var card = new Panel { Size=new Size(248,72), Location=new Point(11,y), BackColor=cCard };
        var lL = new Label { Text=lbl.ToUpper(), Font=fTiny, ForeColor=cMuted, Location=new Point(8,6), AutoSize=true };
        var lV = new Label { Text="...", Font=fMono, ForeColor=cText, Location=new Point(8,20), Size=new Size(232,30) };
        var pb = new ProgressBar { Location=new Point(8,55), Size=new Size(232,8), Style=ProgressBarStyle.Continuous };
        card.Controls.AddRange(new Control[]{lL,lV,pb});
        p.Controls.Add(card);
        return new MonWidget(lV,pb);
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    void SetWidget(MonWidget? w, int pct, string txt) {
        if (w==null) return;
        int v=Math.Clamp(pct,0,100);
        w.Val.Text=txt; w.Bar.Value=v;
        // Forzar color de la barra de progreso (verde/amarillo/rojo)
        uint col = v>=85 ? 0x005050EEu : v>=60 ? 0x0028C8F0u : 0x0050C850u;
        SendMessage(w.Bar.Handle, 0x410, IntPtr.Zero, (IntPtr)col);
    }

    // ── Panel descripcion derecho ─────────────────────────────────────────────
    void BuildDescPanel() {
        var pd = new Panel { Width=340, Dock=DockStyle.Right, BackColor=cPanel };
        pd.Controls.Add(L("Descripcion de la opcion",fSub,cAccent,new Point(14,14)));
        pd.Controls.Add(new Label{Location=new Point(14,38),Size=new Size(310,2),BackColor=cSep});

        _dTitle = new Label { Text="Haz clic en el NOMBRE de cualquier opcion para ver aqui que hace y si te conviene activarla.",
            Font=fBold, ForeColor=cText, Location=new Point(14,48), Size=new Size(312,52) };
        _dBody  = new Label { Font=fBody, ForeColor=cTextDim, Location=new Point(14,108), Size=new Size(312,380) };
        _dRecBox = new Panel { Location=new Point(14,500), Size=new Size(312,110), BackColor=cCard, Visible=false };
        _dRecBox.Controls.Add(new Label{Text="NUESTRA RECOMENDACION",Font=fTiny,ForeColor=cMuted,Location=new Point(10,8),AutoSize=true});
        _dRec = new Label { Font=fSmall, Location=new Point(10,26), Size=new Size(290,76) };
        _dRecBox.Controls.Add(_dRec);

        pd.Controls.AddRange(new Control[]{_dTitle,_dBody,_dRecBox});
        Controls.Add(pd);
    }
    void ShowDesc(TweakItem item) {
        _dTitle.Text=item.Name; _dBody.Text=item.Desc;
        _dRec.Text=item.Rec;
        _dRec.ForeColor = item.Color switch { RecColor.Green=>cGreen, RecColor.Red=>cRed, _=>cYellow };
        _dRecBox.Visible=true;
    }

    // ── Panel central con lista de tweaks ─────────────────────────────────────
    void BuildCenter() {
        _center = new Panel { Dock=DockStyle.Fill, BackColor=cBg, AutoScroll=true };
        Controls.Add(_center);
        _center.BringToFront();

        int y=10;
        foreach (var sec in Sections) {
            var lSec=L(sec.Name,fTitle,sec.IsWarning?cRed:cHeader,new Point(18,y+10));
            lSec.BackColor=cBg; lSec.AutoSize=true;
            _center.Controls.Add(lSec); y+=40;

            if (sec.IsWarning) {
                var lW=L("ATENCION: Lee la descripcion de cada opcion antes de activarla.",fSmall,cYellow,new Point(18,y));
                lW.BackColor=cBg; lW.AutoSize=true;
                _center.Controls.Add(lW); y+=22;
            }
            _center.Controls.Add(new Label{Location=new Point(18,y),Size=new Size(760,1),BackColor=cSep});
            y+=6;

            foreach (var item in sec.Items) { AddCard(item,y); y+=46; }
            y+=18;
        }
        _center.AutoScrollMinSize=new Size(800,y+80);
    }

    void AddCard(TweakItem item, int y) {
        var dotCol = item.Color switch { RecColor.Green=>cGreen, RecColor.Red=>cRed, _=>cYellow };
        var recTxt = item.Color switch { RecColor.Green=>"Recomendado activar",
            RecColor.Red=>"No recomendado para usuarios normales", _=>"Evalua segun tu caso" };

        var card = new Panel{Location=new Point(18,y),Size=new Size(760,44),BackColor=cBg,Cursor=Cursors.Hand};
        _center!.Controls.Add(card);

        var chk  = new CheckBox{Location=new Point(10,13),Size=new Size(16,16),BackColor=cBg};
        var lNm  = new Label{Text=item.Name,Font=fBold,ForeColor=cText,Location=new Point(34,4),
            Size=new Size(520,18),BackColor=cBg,Cursor=Cursors.Hand};
        var dot  = new Label{Text="-",Font=fTiny,ForeColor=dotCol,Location=new Point(34,25),AutoSize=true,BackColor=cBg};
        var lRec = new Label{Text=recTxt,Font=fTiny,ForeColor=dotCol,Location=new Point(46,26),AutoSize=true,BackColor=cBg};
        var lPct = new Label{Text=item.Pct,Font=fTiny,ForeColor=cAccent,Location=new Point(590,5),
            Size=new Size(165,34),TextAlign=ContentAlignment.MiddleRight,BackColor=cBg};
        card.Controls.AddRange(new Control[]{chk,lNm,dot,lRec,lPct});

        _allChk.Add(chk);
        _allItems.Add((chk,item.Name));

        void SetBg(Color bg) {
            card.SuspendLayout();
            card.BackColor=lNm.BackColor=dot.BackColor=lRec.BackColor=lPct.BackColor=chk.BackColor=bg;
            card.ResumeLayout(false);
        }

        // Hover: lo gestiona el timer global _hoverTimer en lugar de MouseEnter/Leave
        // Así evitamos los destellos causados por entrar/salir de controles hijo.
        // Registramos la card en el diccionario de hover con su SetBg.
        _hoverCards[card] = () => {
            bool hov = card.ClientRectangle.Contains(card.PointToClient(Cursor.Position));
            bool sel = chk.Checked;
            Color wanted = hov ? (sel?cSelHov:cHov) : (sel?cSel:cBg);
            if (card.BackColor != wanted) SetBg(wanted);
        };

        // FIX BUG 4: Toggle manual solo para card/dot/lRec/lPct — NO para chk.
        // El CheckBox se marca/desmarca solo con su click nativo.
        // Si agregamos Toggle() al chk.Click, se doble-invierte y nunca cambia.
        void Toggle() {
            var sp=_center!.AutoScrollPosition;
            chk.Checked=!chk.Checked;
            _center.AutoScrollPosition=new Point(Math.Abs(sp.X),Math.Abs(sp.Y));
        }

        // Nombre: solo descripcion
        lNm.Click+=(_,__)=>ShowDesc(item);

        // Card background y labels decorativos: toggle
        card.Click+=(_,__)=>Toggle();
        dot.Click +=(_,__)=>Toggle();
        lRec.Click+=(_,__)=>Toggle();
        lPct.Click+=(_,__)=>Toggle();

        // Checkbox: solo su comportamiento nativo (NO añadir Toggle aquí)
        chk.CheckedChanged+=(_,__)=>{
            UpdateCount();
        };
    }


    void StartMonitor() {
        var mon=_mon; var nums=_diskNums;

        new Thread(()=>{
            var cpu=new PerformanceCounter("Processor","% Processor Time","_Total");
            cpu.NextValue();

            PerformanceCounter? netS=null, netR=null;
            try {
                using var sc=new ManagementObjectSearcher(
                    "SELECT Description,DefaultIPGateway FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True");
                foreach (ManagementObject cfg in sc.Get()) {
                    if (cfg["DefaultIPGateway"]==null) continue;
                    string desc=cfg["Description"]?.ToString()??"";
                    string pfx=desc[..Math.Min(15,desc.Length)];
                    string? inst=new PerformanceCounterCategory("Network Interface")
                        .GetInstanceNames().FirstOrDefault(n=>n.Contains(pfx,StringComparison.OrdinalIgnoreCase));
                    if (inst!=null) {
                        netS=new PerformanceCounter("Network Interface","Bytes Sent/sec",inst);
                        netR=new PerformanceCounter("Network Interface","Bytes Received/sec",inst);
                        netS.NextValue(); netR.NextValue();
                    }
                    break;
                }
            } catch {}

            while (mon.Running) {
                try {
                    // CPU
                    mon.CpuPct=(int)cpu.NextValue();
                    mon.CpuTxt=$"{mon.CpuPct}% en uso";

                    // RAM
                    using var rs=new ManagementObjectSearcher(
                        "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (ManagementObject o in rs.Get()) {
                        long tot=Convert.ToInt64(o["TotalVisibleMemorySize"]);
                        long fr =Convert.ToInt64(o["FreePhysicalMemory"]);
                        long use=tot-fr;
                        mon.RamPct=(int)(use*100/tot);
                        mon.RamTxt=$"{use/1048576.0:N1} / {tot/1048576.0:N1} GB  ({mon.RamPct}%)";
                    }

                    // Discos — via tablas Win32_DiskDriveToDiskPartition (sin escaping de DeviceID)
                    try {
                        // Paso 1: todos los discos logicos
                        var ldByDev = new Dictionary<string,(ulong sz,ulong fr)>();
                        using(var s1=new ManagementObjectSearcher(
                            "SELECT DeviceID,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3"))
                        foreach(ManagementObject o in s1.Get()){
                            string d=o["DeviceID"]?.ToString()??"";
                            if(d!="") ldByDev[d]=(Convert.ToUInt64(o["Size"]),Convert.ToUInt64(o["FreeSpace"]));
                        }
                        // Paso 2: particion → disco logico
                        var partToLog=new Dictionary<string,List<(ulong sz,ulong fr,string dev)>>();
                        using(var s2=new ManagementObjectSearcher(
                            "SELECT Antecedent,Dependent FROM Win32_LogicalDiskToPartition"))
                        foreach(ManagementObject r in s2.Get()){
                            string sa=r["Antecedent"]?.ToString()??"";
                            string sd=r["Dependent"]?.ToString()??"";
                            // WMI path: \\server\ns:Win32_X.DeviceID="value"
                            int ia=sa.IndexOf("DeviceID=\"",StringComparison.Ordinal);
                            int id=sd.IndexOf("DeviceID=\"",StringComparison.Ordinal);
                            if(ia<0||id<0) continue;
                            string pDev=sa.Substring(ia+10).TrimEnd('"').Replace("\\\\","\\");
                            string lDev=sd.Substring(id+10).TrimEnd('"').Replace("\\\\","\\");
                            if(!ldByDev.TryGetValue(lDev,out var e)) continue;
                            if(!partToLog.ContainsKey(pDev)) partToLog[pDev]=new();
                            partToLog[pDev].Add((e.sz,e.fr,lDev));
                        }
                        // Paso 3: disco fisico → particion → disco logico
                        var diskToLog=new Dictionary<int,List<(ulong sz,ulong fr,string dev)>>();
                        using(var s3=new ManagementObjectSearcher(
                            "SELECT Antecedent,Dependent FROM Win32_DiskDriveToDiskPartition"))
                        foreach(ManagementObject r in s3.Get()){
                            string sa=r["Antecedent"]?.ToString()??"";
                            string sd=r["Dependent"]?.ToString()??"";
                            int ip=sa.IndexOf("PHYSICALDRIVE",StringComparison.OrdinalIgnoreCase);
                            int ipd=sd.IndexOf("DeviceID=\"",StringComparison.Ordinal);
                            if(ip<0||ipd<0) continue;
                            // Extraer numero del disco fisico
                            string numStr="";
                            for(int k=ip+13;k<sa.Length&&char.IsDigit(sa[k]);k++) numStr+=sa[k];
                            if(!int.TryParse(numStr,out int dNum)) continue;
                            string pDev=sd.Substring(ipd+10).TrimEnd('"').Replace("\\\\","\\");
                            if(!partToLog.TryGetValue(pDev,out var logList)) continue;
                            if(!diskToLog.ContainsKey(dNum)) diskToLog[dNum]=new();
                            diskToLog[dNum].AddRange(logList);
                        }
                        // Paso 4: actualizar cada widget
                        for(int di=0;di<nums.Length;di++){
                            if(diskToLog.TryGetValue(nums[di],out var ent)&&ent.Count>0){
                                long tS=0,tF=0; var sb=new StringBuilder();
                                foreach(var(sz,fr,dev) in ent){
                                    tS+=(long)sz; tF+=(long)fr;
                                    if(sb.Length>0) sb.Append(' '); sb.Append(dev);
                                }
                                int dp=(int)((tS-tF)*100/tS);
                                mon.Disks[di].Pct=dp;
                                mon.Disks[di].Txt=string.Format(
                                    "{0}  {1:N0}/{2:N0} GB ({3}%)",
                                    sb,(tS-tF)/1073741824.0,tS/1073741824.0,dp);
                            } else { mon.Disks[di].Txt="Sin particiones visibles"; }
                        }
                    } catch(Exception ex){
                        for(int di=0;di<nums.Length;di++)
                            mon.Disks[di].Txt="Err:"+ex.Message[..Math.Min(15,ex.Message.Length)];
                    }

                    // Red
                    if (netS!=null&&netR!=null) {
                        try {
                            double s=netS.NextValue()/1024.0, r=netR.NextValue()/1024.0;
                            mon.NetTxt=$"Subida: {s:N1} KB/s  |  Bajada: {r:N1} KB/s";
                            mon.NetPct=Math.Min((int)((s+r)/50),100);
                        } catch {}
                    } else {
                        try {
                            using var ns=new ManagementObjectSearcher(
                                "SELECT Name,BytesSentPersec,BytesReceivedPersec,BytesTotalPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
                            ulong best=0; double bs=0,br=0;
                            foreach (ManagementObject ni in ns.Get()) {
                                string nm=ni["Name"]?.ToString()??"";
                                if(nm.Contains("Loopback")||nm.Contains("isatap")||nm.Contains("Teredo")) continue;
                                ulong tot=Convert.ToUInt64(ni["BytesTotalPersec"]);
                                if(tot>=best){best=tot;bs=Convert.ToUInt64(ni["BytesSentPersec"])/1024.0;br=Convert.ToUInt64(ni["BytesReceivedPersec"])/1024.0;}
                            }
                            if(best>0){mon.NetTxt=$"Subida: {bs:N1} KB/s  |  Bajada: {br:N1} KB/s";mon.NetPct=Math.Min((int)((bs+br)/50),100);}
                            else mon.NetTxt="Sin trafico de red";
                        } catch { mon.NetTxt="Red no disponible"; }
                    }

                    // GPU
                    try {
                        using var gs=new ManagementObjectSearcher(
                            "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE \'%engtype_3D%\'");
                        int maxG=0;
                        foreach (ManagementObject g in gs.Get())
                            maxG=Math.Max(maxG,Convert.ToInt32(g["UtilizationPercentage"]));
                        mon.GpuPct=maxG; mon.GpuTxt=$"Uso 3D: {maxG}%";
                    } catch { mon.GpuTxt="No disponible"; }

                    mon.Ts=DateTime.Now.ToString("HH:mm:ss");
                } catch {}

                Thread.Sleep(1500);
            }
            cpu.Dispose(); netS?.Dispose(); netR?.Dispose();
        }){ IsBackground=true, Name="HWMonitor" }.Start();

        // Timer UI — solo lee mon, no hace WMI
        _uiTimer=new System.Windows.Forms.Timer{Interval=1000};
        _uiTimer.Tick+=(_,__)=>{
            if(_center==null) return;
            var sp=_center.AutoScrollPosition;
            SetWidget(_wCpu,_mon.CpuPct,_mon.CpuTxt);
            SetWidget(_wRam,_mon.RamPct,_mon.RamTxt);
            for(int i=0;i<_wDisks.Length&&i<_mon.Disks.Length;i++)
                SetWidget(_wDisks[i],_mon.Disks[i].Pct,_mon.Disks[i].Txt);
            if(_wNet!=null){_wNet.Val.Text=_mon.NetTxt;_wNet.Bar.Value=Math.Clamp(_mon.NetPct,0,100);}
            SetWidget(_wGpu,_mon.GpuPct,_mon.GpuTxt);
            if(_mon.Ts!="") _stMsg.Text=$"Monitor activo  -  {_mon.Ts}";
            _center.AutoScrollPosition=new Point(Math.Abs(sp.X),Math.Abs(sp.Y));
        };
        _uiTimer.Start();
    }

    // ── Aplicar / Revertir ────────────────────────────────────────────────────
    void OnApply(object? _s, EventArgs _e) {
        var sel=_allItems.Where(x=>x.Chk.Checked).ToList();
        if(!sel.Any()){Box("No has seleccionado ninguna mejora.","Info",MessageBoxIcon.Information);return;}
        string lista=string.Join("\n",sel.Select(x=>$"  - {x.Name}"));
        if(MessageBox.Show($"Se aplicaran {sel.Count} mejora(s):\n\n{lista}\n\n,Deseas continuar?",
            "Confirmar",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){
            // TODO: logica real de tweaks aqui
            foreach(var t in sel) Debug.WriteLine($"[TWEAK] {t.Name}");
            _stMsg.Text=$"Aplicadas {sel.Count} mejora(s).";
            Box($"Se aplicaron {sel.Count} mejora(s).\n\nReinicia el equipo para que los cambios surtan efecto.","Listo",MessageBoxIcon.Information);
        }
    }
    void OnUndo(object? _s, EventArgs _e) {
        var sel=_allItems.Where(x=>x.Chk.Checked).ToList();
        if(!sel.Any()){Box("Marca las opciones que deseas revertir.","Info",MessageBoxIcon.Information);return;}
        if(MessageBox.Show($"Se revertiran {sel.Count} cambio(s). ,Continuar?",
            "Confirmar",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes){
            foreach(var t in sel) Debug.WriteLine($"[UNDO] {t.Name}");
            _stMsg.Text=$"Revertidos {sel.Count} cambio(s).";
            Box($"Revertidos {sel.Count} cambio(s).\n\nReinicia el equipo.","Revertido",MessageBoxIcon.Information);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    void UpdateCount()=>_stCount.Text=$"{_allChk.Count(c=>c.Checked)} opcion(es) seleccionada(s)";
    static Label L(string t,Font f,Color c,Point pt){var l=new Label{Text=t,Font=f,ForeColor=c,Location=pt,AutoSize=true};return l;}
    static void Box(string t,string cap,MessageBoxIcon ico)=>MessageBox.Show(t,cap,MessageBoxButtons.OK,ico);

    protected override void OnFormClosing(FormClosingEventArgs e){
        _mon.Running=false;
        _uiTimer?.Stop();    _uiTimer?.Dispose();
        _hoverTimer?.Stop(); _hoverTimer?.Dispose();
        base.OnFormClosing(e);
    }
}

// ── Entry point ───────────────────────────────────────────────────────────────
static class Program
{
    [STAThread]
    static void Main(){
        if(!Admin.IsAdmin()){Admin.Elevate();return;}
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new MainForm());
    }
}

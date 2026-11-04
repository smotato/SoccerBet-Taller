// SoccerBet - reglas del futbol y reglas de apuestas (RF8). Se ejecuta solo la primera vez que se crea el volumen.
db = db.getSiblingDB('soccerbet');
const D = 1;   // id_deporte = 1 (Futbol) en MySQL
const reglas = [
  // ---- Reglas del futbol ----
  ['DEPORTE', 1, 'Duracion del partido', 'Un partido dura 90 minutos, divididos en dos tiempos de 45 minutos con un descanso de 15. El arbitro suma tiempo añadido al final de cada tiempo por las interrupciones.'],
  ['DEPORTE', 2, 'Los equipos', 'Cada equipo juega con 11 jugadores, uno de ellos portero. Un partido no puede continuar si un equipo se queda con menos de 7.'],
  ['DEPORTE', 3, 'El gol', 'Hay gol cuando el balon cruza completamente la linea de meta, entre los postes y bajo el travesaño, sin que antes se haya cometido una infraccion.'],
  ['DEPORTE', 4, 'Fuera de juego', 'Un jugador esta en fuera de juego si, al recibir el pase, esta mas cerca de la linea de meta rival que el balon y que el penultimo defensor. No es infraccion recibir directamente de un saque de meta, un saque de banda o un saque de esquina.'],
  ['DEPORTE', 5, 'Faltas y tarjetas', 'La tarjeta amarilla es una amonestacion. Dos amarillas en un partido producen la expulsion. La tarjeta roja expulsa al jugador de inmediato y su equipo continua con un jugador menos.'],
  ['DEPORTE', 6, 'Tiro penal', 'Se concede cuando un defensor comete una falta sancionable con tiro libre directo dentro de su propia area. Se ejecuta desde el punto penal, solo con el portero defendiendo.'],
  ['DEPORTE', 7, 'Cambios', 'Cada equipo puede hacer hasta 5 sustituciones en un partido. Un jugador sustituido no puede volver a entrar.'],
  ['DEPORTE', 8, 'Puntos de la liga', 'En la liga, la victoria vale 3 puntos, el empate 1 y la derrota 0. La tabla se ordena por puntos, diferencia de gol y goles a favor.'],
  // ---- Reglas de apuestas de SoccerBet ----
  ['APUESTAS', 1, 'Dinero de prueba', 'SoccerBet es un proyecto academico. Las recargas y los retiros son simulados y no se mueve dinero real.'],
  ['APUESTAS', 2, 'Mercados disponibles', 'Cada partido ofrece tres mercados: 1X2 (quien gana: local, empate o visitante), Total de goles 2.5 (mas o menos de 2.5 goles) y Ambos equipos anotan (si o no). El resultado se decide con los 90 minutos.'],
  ['APUESTAS', 3, 'Apuesta sencilla y combinada', 'Con una sola seleccion la apuesta es sencilla. Con 2 a 10 selecciones es combinada: la cuota total es el producto de las cuotas y se gana solo si todas las selecciones aciertan.'],
  ['APUESTAS', 4, 'Una seleccion por mercado', 'En un mismo partido puedes elegir como maximo una seleccion por mercado (hasta tres por partido). Asi no se pueden combinar resultados que se excluyen entre si, como Local y Empate. SoccerBet no calcula la relacion entre mercados del mismo partido.'],
  ['APUESTAS', 5, 'Limites', 'El importe va de $ 1.000 a $ 500.000 por apuesta, y el premio maximo por apuesta es de $ 20.000.000. Si el premio calculado lo supera, se paga el maximo y el cupon te avisa antes de confirmar.'],
  ['APUESTAS', 6, 'La cuota', 'La cuota que se paga es la vigente al confirmar la apuesta y no cambia despues. Si cambio mientras armabas el cupon, SoccerBet te muestra la nueva para que la aceptes o canceles.'],
  ['APUESTAS', 7, 'Apuestas en vivo y cierre de mercados', 'En los partidos en vivo las cuotas se recalculan segun el marcador y el minuto. Los mercados se cierran unos segundos despues de cada gol y a partir del minuto 85, hasta que termina el partido.'],
  ['APUESTAS', 8, 'Cash out', 'Mientras una apuesta esta pendiente puedes cerrarla y cobrar un valor calculado al momento con las cuotas vigentes, que incluye un margen de la casa del 5 %. Si el valor es menor a $ 1 o el mercado esta cerrado, el boton se bloquea.'],
  ['APUESTAS', 9, 'Partidos anulados', 'Si un partido se anula, su seleccion sale de la combinada (cuenta como cuota 1,00). Si todas las selecciones se anulan, o la apuesta era sencilla, se devuelve el importe.'],
  ['APUESTAS', 10, 'Liquidacion', 'Al terminar el partido, las apuestas se resuelven automaticamente y el premio se acredita al saldo. Una combinada pierde en cuanto una de sus selecciones pierde.'],
  ['APUESTAS', 11, 'Retiros', 'Puedes retirar desde $ 10.000 hasta tu saldo disponible. El retiro se aprueba de inmediato y descuenta el saldo una sola vez.']
];
reglas.forEach(([categoria, orden, nombre, descripcion]) => {
  db.reglas.updateOne({ id_deporte: D, nombre }, { $set: { id_deporte: D, nombre, descripcion, activa: true, categoria, orden } }, { upsert: true });
});
print('reglas: ' + db.reglas.countDocuments({ id_deporte: D }));

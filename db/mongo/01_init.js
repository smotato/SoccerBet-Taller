// SoccerBet - MongoDB 7 (mongosh): eventos de partido, alineaciones y reglas
db = db.getSiblingDB('soccerbet');

db.createCollection('eventos_partido', { validator: { $jsonSchema: {
  bsonType: 'object', required: ['id_partido', 'tipo_evento', 'minuto'],
  properties: {
    id_partido: { bsonType: 'number', description: 'ID logico -> PostgreSQL.partido' },
    id_equipo:  { bsonType: ['number', 'null'], description: 'ID logico -> MySQL.equipo' },
    id_jugador: { bsonType: ['number', 'null'], description: 'ID logico -> MySQL.jugador' },
    tipo_evento: { bsonType: 'string' },          // GOL, TARJETA_AMARILLA, TARJETA_ROJA, SUSTITUCION...
    minuto: { bsonType: 'number', minimum: 0 },
    minuto_adicional: { bsonType: ['number', 'null'] },
    descripcion: { bsonType: ['string', 'null'] },
    creado: { bsonType: 'date' }
  } } } });
db.eventos_partido.createIndex({ id_partido: 1, minuto: 1 });
db.eventos_partido.createIndex({ id_jugador: 1, tipo_evento: 1 });

// Alineacion + Alineacion_Detalle en un solo documento (jugadores anidados)
db.createCollection('alineaciones', { validator: { $jsonSchema: {
  bsonType: 'object', required: ['id_partido', 'id_equipo', 'tipo', 'jugadores'],
  properties: {
    id_partido: { bsonType: 'number' }, id_equipo: { bsonType: 'number' },
    tipo: { bsonType: 'string' }, formacion: { bsonType: ['string', 'null'] },
    jugadores: { bsonType: 'array', items: { bsonType: 'object', required: ['id_jugador', 'titular'],
      properties: {
        id_jugador: { bsonType: 'number' }, titular: { bsonType: 'bool' },
        numero_camiseta: { bsonType: ['number', 'null'] }, posicion: { bsonType: ['string', 'null'] },
        minuto_entrada: { bsonType: ['number', 'null'] }, minuto_salida: { bsonType: ['number', 'null'] }
      } } }
  } } } });
db.alineaciones.createIndex({ id_partido: 1, id_equipo: 1, tipo: 1 }, { unique: true });

db.createCollection('reglas', { validator: { $jsonSchema: {
  bsonType: 'object', required: ['id_deporte', 'nombre', 'descripcion', 'activa'],
  properties: { id_deporte: { bsonType: 'number', description: 'ID logico -> MySQL.deporte' },
    nombre: { bsonType: 'string' }, descripcion: { bsonType: 'string' }, activa: { bsonType: 'bool' } } } } });
db.reglas.createIndex({ id_deporte: 1, nombre: 1 }, { unique: true });
